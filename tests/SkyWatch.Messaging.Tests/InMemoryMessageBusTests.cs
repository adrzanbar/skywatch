using Xunit;
using SkyWatch.Messaging;

namespace SkyWatch.Messaging.Tests;

public sealed class InMemoryMessageBusTests
{
    [Fact]
    public async Task Publish_delivers_record_to_subscriber()
    {
        await using var bus = new InMemoryMessageBus();
        var received = new TaskCompletionSource<BusRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateMessageHandler((delivery, _) =>
        {
            received.TrySetResult(delivery.Record);
            return Task.FromResult(MessageResult.Ack);
        });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task subscription = bus.SubscribeAsync("topic", "group", handler, cancellation.Token);

        var record = new BusRecord("id-1", "key", [1, 2, 3], DateTimeOffset.UtcNow);
        await bus.PublishAsync("topic", record, cancellation.Token);

        BusRecord delivered = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(record.Id, delivered.Id);
        Assert.Equal(record.Key, delivered.Key);
        Assert.Equal(record.Payload, delivered.Payload);

        await cancellation.CancelAsync();
        await subscription;
    }

    [Fact]
    public async Task Dead_letter_result_is_recorded()
    {
        await using var bus = new InMemoryMessageBus();
        var handled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new DelegateMessageHandler((_, _) =>
        {
            handled.TrySetResult();
            return Task.FromResult(MessageResult.DeadLetter);
        });

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task subscription = bus.SubscribeAsync("topic", "group", handler, cancellation.Token);

        await bus.PublishAsync(
            "topic",
            new BusRecord("bad", "key", [9], DateTimeOffset.UtcNow),
            cancellation.Token);

        await handled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(bus.DeadLetters);

        await cancellation.CancelAsync();
        await subscription;
    }
}
