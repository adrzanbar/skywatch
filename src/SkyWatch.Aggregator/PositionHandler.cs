using System.Text.Json;
using SkyWatch.Analytics;
using SkyWatch.Contracts;
using SkyWatch.Messaging;
using SkyWatch.Observability;
using Microsoft.Extensions.Logging;

namespace SkyWatch.Aggregator;

public sealed class PositionHandler : IMessageHandler
{
    private readonly AirportAggregator _aggregator;
    private readonly ILogger<PositionHandler> _logger;

    public PositionHandler(AirportAggregator aggregator, ILogger<PositionHandler> logger)
    {
        _aggregator = aggregator;
        _logger = logger;
    }

    public Task<MessageResult> HandleAsync(
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
            return Task.FromResult(MessageResult.DeadLetter);
        }

        if (position is null)
        {
            return Task.FromResult(MessageResult.DeadLetter);
        }

        SkyWatchTelemetry.PositionsConsumed.Add(1);
        _aggregator.Process(position);
        return Task.FromResult(MessageResult.Ack);
    }
}
