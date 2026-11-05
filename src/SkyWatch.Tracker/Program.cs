using Microsoft.Extensions.Options;
using SkyWatch.Analytics;
using SkyWatch.Messaging.Hosting;
using SkyWatch.Tracker;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<TrackerOptions>(builder.Configuration.GetSection("Tracker"));
builder.Services.AddSingleton(serviceProvider =>
    new AircraftTracker(serviceProvider.GetRequiredService<IOptions<TrackerOptions>>().Value));
builder.Services.AddSingleton<PositionHandler>();
builder.Services.AddSkyWatchMessaging(builder.Configuration);
builder.Services.AddHostedService<TrackerWorker>();

var host = builder.Build();
await host.RunAsync();
