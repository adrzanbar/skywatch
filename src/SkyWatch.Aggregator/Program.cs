using Microsoft.Extensions.Options;
using SkyWatch.Aggregator;
using SkyWatch.Analytics;
using SkyWatch.Messaging.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<AirportOptions>(builder.Configuration.GetSection("Airports"));
builder.Services.Configure<AggregatorOptions>(builder.Configuration.GetSection("Aggregator"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(serviceProvider =>
    new AirportAggregator(serviceProvider.GetRequiredService<IOptions<AirportOptions>>().Value));
builder.Services.AddSingleton<PositionHandler>();
builder.Services.AddSkyWatchMessaging(builder.Configuration);
builder.Services.AddHostedService<AggregatorWorker>();

var host = builder.Build();
await host.RunAsync();
