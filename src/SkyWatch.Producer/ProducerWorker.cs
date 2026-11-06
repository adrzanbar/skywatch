using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyWatch.Contracts;
using SkyWatch.Messaging;
using SkyWatch.Observability;
using SkyWatch.Sources.AdsbLol;

namespace SkyWatch.Producer;

public sealed class ProducerWorker : BackgroundService
{
    private readonly IAircraftSource _source;
    private readonly IMessageBus _bus;
    private readonly ProducerOptions _options;
    private readonly ILogger<ProducerWorker> _logger;

    public ProducerWorker(
        IAircraftSource source,
        IMessageBus bus,
        IOptions<ProducerOptions> options,
        ILogger<ProducerWorker> logger)
    {
        _source = source;
        _bus = bus;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (AircraftSnapshot snapshot in _source.ReadAsync(stoppingToken).ConfigureAwait(false))
        {
            int published = 0;
            int errors = 0;

            await Parallel.ForEachAsync(
                snapshot.Positions,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, _options.MaxDegreeOfParallelism),
                    CancellationToken = stoppingToken
                },
                async (position, cancellationToken) =>
                {
                    try
                    {
                        byte[] payload = SkyWatchJson.Serialize(position);
                        var record = new BusRecord(
                            position.EventId,
                            position.Hex,
                            payload,
                            position.SnapshotAt);

                        await _bus.PublishAsync(Topics.Positions, record, cancellationToken)
                            .ConfigureAwait(false);

                        Interlocked.Increment(ref published);
                        SkyWatchTelemetry.PositionsPublished.Add(1);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Shutdown.
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref errors);
                        SkyWatchTelemetry.Errors.Add(1);
                        _logger.LogError(ex, "Could not publish position for {Hex}.", position.Hex);
                    }
                }).ConfigureAwait(false);

            _logger.LogInformation(
                "Snapshot {SnapshotAt:O}: published {Published}/{Total} positions, {Errors} errors.",
                snapshot.SnapshotAt,
                published,
                snapshot.Positions.Count,
                errors);
        }
    }
}
