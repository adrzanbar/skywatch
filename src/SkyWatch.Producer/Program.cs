using Microsoft.Extensions.Options;
using SkyWatch.Messaging.Hosting;
using SkyWatch.Sources.AdsbLol;
using Microsoft.Extensions.Hosting;
using SkyWatch.Observability;
using SkyWatch.Producer;
using Microsoft.Extensions.DependencyInjection;

var builder = Host.CreateApplicationBuilder(args);
builder.AddSkyWatchObservability("skywatch-producer");

builder.Services.Configure<AdsbLolOptions>(builder.Configuration.GetSection("Adsb"));
builder.Services.Configure<ProducerOptions>(builder.Configuration.GetSection("Producer"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHttpClient<IAircraftSource, AdsbLolSource>((serviceProvider, client) =>
{
    AdsbLolOptions options = serviceProvider.GetRequiredService<IOptions<AdsbLolOptions>>().Value;
    client.Timeout = TimeSpan.FromSeconds(30);
    client.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");
});
builder.Services.AddSkyWatchMessaging(builder.Configuration);
builder.Services.AddHostedService<ProducerWorker>();

var host = builder.Build();
await host.RunAsync();
