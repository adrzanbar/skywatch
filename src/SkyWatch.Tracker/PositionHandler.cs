using System.Text.Json;
using SkyWatch.Analytics;
using SkyWatch.Contracts;
using SkyWatch.Messaging;
using SkyWatch.Observability;
using Microsoft.Extensions.Logging;

namespace SkyWatch.Tracker;

public sealed class PositionHandler : IMessageHandler
{
    private readonly AircraftTracker _tracker;
    private readonly IMessageBus _bus;
    private readonly ILogger<PositionHandler> _logger;

    public PositionHandler(
        AircraftTracker tracker,
        IMessageBus bus,
        ILogger<PositionHandler> logger)
    {
        _tracker = tracker;
        _bus = bus;
        _logger = logger;
    }

    public async Task<MessageResult> HandleAsync(
        BusDelivery delivery,
        CancellationToken cancellationToken)
    {
        AircraftPosition? position;

        try
        {
            position = SkyWatchJson.Deserialize<AircraftPosition>(delivery.Record.Payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid position payload at {Topic}/{Offset}.", delivery.Topic, delivery.Offset);
            return MessageResult.DeadLetter;
        }

        if (position is null)
        {
            return MessageResult.DeadLetter;
        }

        SkyWatchTelemetry.PositionsConsumed.Add(1);
        IReadOnlyList<FlightAlert> alerts = _tracker.Process(position);

        if (alerts.Count > 0)
        {
            SkyWatchTelemetry.AlertsPublished.Add(alerts.Count);
        }

        foreach (FlightAlert alert in alerts)
        {
            byte[] payload = SkyWatchJson.Serialize(alert);
            var record = new BusRecord(
                $"{alert.AlertId}:{alert.Status}",
                alert.Hex,
                payload,
                alert.ClosedAt ?? alert.OpenedAt);

            await _bus.PublishAsync(Topics.Alerts, record, cancellationToken).ConfigureAwait(false);
        }

        return MessageResult.Ack;
    }
}
