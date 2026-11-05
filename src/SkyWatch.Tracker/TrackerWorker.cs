using SkyWatch.Contracts;
using SkyWatch.Messaging;
using Microsoft.Extensions.Hosting;

namespace SkyWatch.Tracker;

public sealed class TrackerWorker : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly PositionHandler _handler;

    public TrackerWorker(IMessageBus bus, PositionHandler handler)
    {
        _bus = bus;
        _handler = handler;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        _bus.SubscribeAsync(Topics.Positions, "skywatch-tracker", _handler, stoppingToken);
}
