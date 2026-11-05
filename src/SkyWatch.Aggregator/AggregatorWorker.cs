using Microsoft.Extensions.Options;
using SkyWatch.Analytics;
using SkyWatch.Contracts;
using SkyWatch.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SkyWatch.Aggregator;

public sealed class AggregatorWorker : BackgroundService
{
    private readonly IMessageBus _bus;
    private readonly AirportAggregator _aggregator;
    private readonly PositionHandler _handler;
    private readonly AggregatorOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AggregatorWorker> _logger;

    public AggregatorWorker(
        IMessageBus bus,
        AirportAggregator aggregator,
        PositionHandler handler,
        IOptions<AggregatorOptions> options,
        TimeProvider timeProvider,
        ILogger<AggregatorWorker> logger)
    {
        _bus = bus;
        _aggregator = aggregator;
        _handler = handler;
        _options = options.Value;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);

        Task subscribe = _bus.SubscribeAsync(
            Topics.Positions,
            "skywatch-aggregator",
            _handler,
            linked.Token);

        Task publish = PublishSnapshotsAsync(linked.Token);

        try
        {
            await Task.WhenAll(subscribe, publish).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown.
        }
        finally
        {
            await linked.CancelAsync().ConfigureAwait(false);
        }
    }

    private async Task PublishSnapshotsAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(
            TimeSpan.FromSeconds(Math.Max(1, _options.SnapshotSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                AirportAggregationResult result = _aggregator.Snapshot(_timeProvider.GetUtcNow());

                foreach (AirportCongestion airport in result.Airports)
                {
                    await PublishAsync(
                        Topics.Stats,
                        airport.AirportCode,
                        airport.WindowEnd,
                        airport,
                        cancellationToken).ConfigureAwait(false);
                }

                await PublishAsync(
                    Topics.Stats,
                    "global",
                    result.Fleet.WindowEnd,
                    result.Fleet,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Published stats for {Airports} airports and {Aircraft} aircraft.",
                    result.Airports.Count,
                    result.Fleet.Aircraft);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutdown.
        }
    }

    private Task PublishAsync<T>(
        string topic,
        string key,
        DateTimeOffset timestamp,
        T value,
        CancellationToken cancellationToken)
    {
        byte[] payload = SkyWatchJson.Serialize(value);
        var record = new BusRecord(
            $"{topic}:{key}:{timestamp.ToUnixTimeMilliseconds()}",
            key,
            payload,
            timestamp);

        return _bus.PublishAsync(topic, record, cancellationToken);
    }
}
