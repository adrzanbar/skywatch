using System.Collections.Concurrent;
using System.Threading.Channels;

namespace SkyWatch.Messaging;

/// <summary>
/// Test double and local smoke-test bus. It broadcasts every record to every
/// subscriber; it deliberately does not model consumer groups or persistence.
/// </summary>
public sealed class InMemoryMessageBus : IMessageBus, IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Channel<BusRecord>>> _subscribers = new(StringComparer.Ordinal);
    private readonly ConcurrentBag<BusRecord> _deadLetters = [];

    public IReadOnlyCollection<BusRecord> DeadLetters => _deadLetters;

    public Task PublishAsync(string topic, BusRecord record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(record);

        if (_subscribers.TryGetValue(topic, out var subscribers))
        {
            foreach (var channel in subscribers.Values)
            {
                channel.Writer.TryWrite(record);
            }
        }

        return Task.CompletedTask;
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

        Guid id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<BusRecord>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers.GetOrAdd(topic, _ => new ConcurrentDictionary<Guid, Channel<BusRecord>>())[id] = channel;

        try
        {
            await foreach (var record in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var delivery = new BusDelivery(topic, 0, 0, record, 1);
                MessageResult result;

                try
                {
                    result = await handler.HandleAsync(delivery, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    _deadLetters.Add(record);
                    continue;
                }

                if (result == MessageResult.DeadLetter)
                {
                    _deadLetters.Add(record);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            if (_subscribers.TryGetValue(topic, out var subscribers))
            {
                subscribers.TryRemove(id, out _);
            }

            channel.Writer.TryComplete();
        }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var subscribers in _subscribers.Values)
        {
            foreach (var channel in subscribers.Values)
            {
                channel.Writer.TryComplete();
            }
        }

        _subscribers.Clear();
        return ValueTask.CompletedTask;
    }
}
