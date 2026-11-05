using Xunit;
using SkyWatch.Messaging;

namespace SkyWatch.Messaging.Tests;

internal sealed class DelegateMessageHandler(
    Func<BusDelivery, CancellationToken, Task<MessageResult>> handler) : IMessageHandler
{
    public Task<MessageResult> HandleAsync(BusDelivery delivery, CancellationToken cancellationToken) =>
        handler(delivery, cancellationToken);
}
