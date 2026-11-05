using System.Text;
using Confluent.Kafka;
using Xunit;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Logging.Abstractions;
using SkyWatch.Messaging;
using SkyWatch.Messaging.Kafka;

namespace SkyWatch.Messaging.Tests;

/// <summary>
/// Optional live contract test. Set SKYWATCH_KAFKA_BOOTSTRAP to run it, for example
/// SKYWATCH_KAFKA_BOOTSTRAP=10.66.51.35:9094.
/// </summary>
public sealed class KafkaMessageBusContractTests
{
    [Fact]
    public async Task Publish_and_consume_roundtrip()
    {
        string? bootstrap = Environment.GetEnvironmentVariable("SKYWATCH_KAFKA_BOOTSTRAP");

        if (string.IsNullOrWhiteSpace(bootstrap))
        {
            // Opt-in integration test; set SKYWATCH_KAFKA_BOOTSTRAP to run it.
            return;
        }

        string topic = $"skywatch-contract-{Guid.NewGuid():N}";
        var bus = new KafkaMessageBus(
            new KafkaBusOptions
            {
                BootstrapServers = bootstrap,
                ClientId = "skywatch-contract",
                MaxAttempts = 2,
                RetryDelayMs = 100
            },
            NullLogger<KafkaMessageBus>.Instance);

        await using (bus)
        {
            await CreateTopicAsync(bootstrap, topic);

            try
            {
                var record = new BusRecord(
                    "contract-1",
                    "abc123",
                    Encoding.UTF8.GetBytes("{\"hex\":\"abc123\"}"),
                    DateTimeOffset.UtcNow,
                    new Dictionary<string, string> { ["event-id"] = "contract-1" });

                await bus.PublishAsync(topic, record, CancellationToken.None);

                var received = new TaskCompletionSource<BusDelivery>(TaskCreationOptions.RunContinuationsAsynchronously);
                using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                Task subscription = bus.SubscribeAsync(
                    topic,
                    $"skywatch-contract-{Guid.NewGuid():N}",
                    new DelegateMessageHandler((delivery, _) =>
                    {
                        received.TrySetResult(delivery);
                        return Task.FromResult(MessageResult.Ack);
                    }),
                    cancellation.Token);

                BusDelivery delivery = await received.Task.WaitAsync(TimeSpan.FromSeconds(30));
                Assert.Equal("abc123", delivery.Record.Key);
                Assert.Equal("{\"hex\":\"abc123\"}", Encoding.UTF8.GetString(delivery.Record.Payload));
                Assert.Equal("contract-1", delivery.Record.Headers?["event-id"]);

                await cancellation.CancelAsync();
                await subscription;
            }
            finally
            {
                await DeleteTopicAsync(bootstrap, topic);
            }
        }
    }

    private static async Task CreateTopicAsync(string bootstrap, string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();

        await admin.CreateTopicsAsync(
        [
            new TopicSpecification
            {
                Name = topic,
                NumPartitions = 1,
                ReplicationFactor = 1
            }
        ]);
    }

    private static async Task DeleteTopicAsync(string bootstrap, string topic)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = bootstrap }).Build();

        try
        {
            await admin.DeleteTopicsAsync([topic]);
        }
        catch (DeleteTopicsException)
        {
            // Best-effort cleanup.
        }
    }
}
