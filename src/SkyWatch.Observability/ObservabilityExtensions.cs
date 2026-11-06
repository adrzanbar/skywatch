using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SkyWatch.Observability;

public static class ObservabilityExtensions
{
    /// <summary>
    /// Adds OTLP traces and a Prometheus metrics endpoint. Both are optional:
    /// an empty OTLP endpoint disables trace export, and the metrics listener
    /// is what Prometheus scrapes on the pod IP.
    /// </summary>
    public static IHostApplicationBuilder AddSkyWatchObservability(
        this IHostApplicationBuilder builder,
        string serviceName)
    {
        ArgumentNullException.ThrowIfNull(builder);

        string? otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
        int metricsPort = int.TryParse(
            builder.Configuration["Observability:MetricsPort"],
            out int configuredPort)
            ? configuredPort
            : 9100;

        // HttpListener on Linux rejects "0.0.0.0", "*" and "+". In Kubernetes
        // the deployment injects the pod IP through Observability__Host; local
        // runs fall back to localhost.
        string metricsHost = builder.Configuration["Observability:Host"] ?? string.Empty;

        if (string.IsNullOrWhiteSpace(metricsHost) || metricsHost is "*" or "+" or "0.0.0.0")
        {
            metricsHost = Environment.GetEnvironmentVariable("POD_IP") ?? "localhost";
        }

        ResourceBuilder resource = ResourceBuilder.CreateDefault().AddService(serviceName);

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing =>
            {
                tracing
                    .SetResourceBuilder(resource)
                    .AddSource(SkyWatchTelemetry.ActivitySourceName)
                    .AddHttpClientInstrumentation();

                if (!string.IsNullOrWhiteSpace(otlpEndpoint))
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resource)
                    .AddMeter(SkyWatchTelemetry.MeterName)
                    .AddPrometheusHttpListener(options =>
                    {
                        options.Host = metricsHost;
                        options.Port = metricsPort;
                    });
            });

        return builder;
    }
}

public static class SkyWatchTelemetry
{
    public const string ActivitySourceName = "SkyWatch";

    public const string MeterName = "SkyWatch";

    public static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    public static readonly Meter Meter = new(MeterName);

    public static readonly Counter<long> PositionsPublished =
        Meter.CreateCounter<long>("skywatch.positions.published");

    public static readonly Counter<long> PositionsConsumed =
        Meter.CreateCounter<long>("skywatch.positions.consumed");

    public static readonly Counter<long> AlertsPublished =
        Meter.CreateCounter<long>("skywatch.alerts.published");

    public static readonly Counter<long> StatsPublished =
        Meter.CreateCounter<long>("skywatch.stats.published");

    public static readonly Counter<long> Errors =
        Meter.CreateCounter<long>("skywatch.errors");
}
