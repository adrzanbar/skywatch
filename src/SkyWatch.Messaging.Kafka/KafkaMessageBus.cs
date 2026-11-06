using System.Collections.Concurrent;
using System.Text;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using SkyWatch.Contracts;
using SkyWatch.Messaging;

namespace SkyWatch.Messaging.Kafka;

/// <summary>Confluent.Kafka implementation of the broker-neutral message bus.</summary>
public sealed class KafkaMessageBus : IMessageBus, IAsyncDisposable
{
    private readonly KafkaBusOptions _options;
    private readonly ILogger<KafkaMessageBus> _logger;
    private readonly IProducer<string, byte[]> _producer;
    private readonly ConcurrentDictionary<string, int> _attempts = new(StringComparer.Ordinal);

    public KafkaMessageBus(KafkaBusOptions options, ILogger<KafkaMessageBus> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;

        var config = new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ClientId,
            Acks = Acks.All,
            EnableIdempotence = true,
            LingerMs = 5
        };

        _producer = new ProducerBuilder<string, byte[]>(config).Build();
    }

    public async Task PublishAsync(string topic, BusRecord record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(record);

        var message = new Message<string, byte[]>
        {
            Key = record.Key,
            Value = record.Payload,
            Timestamp = new Timestamp(record.Timestamp.UtcDateTime)
        };

        if (record.Headers is { Count: > 0 })
        {
            message.Headers = new Headers();
            foreach (var (key, value) in record.Headers)
            {
                message.Headers.Add(key, Encoding.UTF8.GetBytes(value));
            }
        }

        DeliveryResult<string, byte[]> result =
            await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);

        if (result.Status == PersistenceStatus.NotPersisted)
        {
            throw new InvalidOperationException($"Kafka did not persist record '{record.Id}'.");
        }
    }

    public async Task SubscribeAsync(
        string topic,
        string group,
        IMessageHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(handler);

        // The consume loop blocks between messages. Yield once so callers can start
        // their own background loops before this subscription takes over the thread.
        await Task.Yield();

        var config = new ConsumerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            ClientId = _options.ClientId,
            GroupId = group,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = false
        };

        using var consumer = new ConsumerBuilder<string, byte[]>(config).Build();
        consumer.Subscribe(topic);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ConsumeResult<string, byte[]>? result;

                try
                {
                    result = consumer.Consume(TimeSpan.FromMilliseconds(500));
                }
                catch (ConsumeException ex)
                {
                    _logger.LogError(ex, "Kafka consume failed on topic {Topic}.", topic);
                    continue;
                }

                if (result is null)
                {
                    continue;
                }

                BusRecord record = ToRecord(topic, result);
                string attemptKey = AttemptKey(result);
                int attempt = _attempts.GetValueOrDefault(attemptKey, 1);
                var delivery = new BusDelivery(
                    topic,
                    result.Partition.Value,
                    result.Offset.Value,
                    record,
                    attempt);

                try
                {
                    MessageResult outcome = await handler
                        .HandleAsync(delivery, cancellationToken)
                        .ConfigureAwait(false);

                    if (outcome == MessageResult.DeadLetter)
                    {
                        await MoveToDeadLetterAsync(record, cancellationToken).ConfigureAwait(false);
                    }

                    consumer.Commit(result);
                    _attempts.TryRemove(attemptKey, out _);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    int nextAttempt = _attempts.AddOrUpdate(attemptKey, 1, static (_, value) => value + 1);
                    _logger.LogError(
                        ex,
                        "Handler failed for {Topic}/{Partition}/{Offset} (attempt {Attempt}/{MaxAttempts}).",
                        topic,
                        result.Partition.Value,
                        result.Offset.Value,
                        nextAttempt,
                        _options.MaxAttempts);

                    if (nextAttempt >= _options.MaxAttempts)
                    {
                        await MoveToDeadLetterAsync(record, cancellationToken).ConfigureAwait(false);
                        consumer.Commit(result);
                        _attempts.TryRemove(attemptKey, out _);
                    }
                    else
                    {
                        consumer.Seek(result.TopicPartitionOffset);
                        await Task.Delay(_options.RetryDelayMs, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    public ValueTask DisposeAsync()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
        return ValueTask.CompletedTask;
    }

    private Task MoveToDeadLetterAsync(BusRecord record, CancellationToken cancellationToken) =>
        PublishAsync(Topics.DeadLetter, record, cancellationToken);

    private static string AttemptKey(ConsumeResult<string, byte[]> result) =>
        $"{result.Topic}:{result.Partition.Value}:{result.Offset.Value}";

    private static BusRecord ToRecord(string topic, ConsumeResult<string, byte[]> result)
    {
        Dictionary<string, string>? headers = null;

        if (result.Message.Headers is { Count: > 0 })
        {
            headers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (IHeader header in result.Message.Headers)
            {
                headers[header.Key] = Encoding.UTF8.GetString(header.GetValueBytes());
            }
        }

        return new BusRecord(
            $"{topic}:{result.Partition.Value}:{result.Offset.Value}",
            result.Message.Key ?? string.Empty,
            result.Message.Value ?? [],
            DateTimeOffset.FromUnixTimeMilliseconds(result.Message.Timestamp.UnixTimestampMs),
            headers);
    }
}
