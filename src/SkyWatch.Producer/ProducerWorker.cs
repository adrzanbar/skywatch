using SkyWatch.Contracts;
using SkyWatch.Messaging;
using SkyWatch.Sources.AdsbLol;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkyWatch.Producer;

public sealed class ProducerWorker : BackgroundService
{
    private readonly IAircraftSource _source;
    private readonly IMessageBus _bus;
    private readonly ILogger<ProducerWorker> _logger;

    public ProducerWorker(
        IAircraftSource source,
        IMessageBus bus,
        ILogger<ProducerWorker> logger)
    {
        _source = source;
        _bus = bus;
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
                    MaxDegreeOfParallelism = 32,
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
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // Shutdown.
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref errors);
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
