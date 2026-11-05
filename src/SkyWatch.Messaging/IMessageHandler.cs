namespace SkyWatch.Messaging;

public interface IMessageHandler
{
    Task<MessageResult> HandleAsync(BusDelivery delivery, CancellationToken cancellationToken);
}
