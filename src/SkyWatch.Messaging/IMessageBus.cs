namespace SkyWatch.Messaging;

/// <summary>
/// The only messaging type application code sees. Kafka and K0sStreams implement it
/// behind a mode switch; no broker-specific type leaks into producers or consumers.
/// </summary>
public interface IMessageBus
{
    Task PublishAsync(string topic, BusRecord record, CancellationToken cancellationToken);

    Task SubscribeAsync(
        string topic,
        string group,
        IMessageHandler handler,
        CancellationToken cancellationToken);
}
