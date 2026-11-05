using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkyWatch.Messaging;
using SkyWatch.Messaging.K0sStreams;
using SkyWatch.Messaging.Kafka;

namespace SkyWatch.Messaging.Hosting;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the one bus adapter selected by <c>Messaging:Mode</c>. This is the
    /// only place in the application that knows about concrete brokers.
    /// </summary>
    public static IServiceCollection AddSkyWatchMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<MessageBusOptions>(configuration.GetSection("Messaging"));

        services.AddHttpClient(K0sStreamsMessageBus.HttpClientName, (serviceProvider, client) =>
        {
            MessageBusOptions options = serviceProvider
                .GetRequiredService<IOptions<MessageBusOptions>>()
                .Value;

            client.Timeout = TimeSpan.FromSeconds(Math.Max(1, options.K0sStreams.HttpTimeoutSeconds));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("skywatch/0.1");
        });

        services.AddSingleton<IMessageBus>(serviceProvider =>
        {
            MessageBusOptions options = serviceProvider
                .GetRequiredService<IOptions<MessageBusOptions>>()
                .Value;

            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            return options.ResolveMode() switch
            {
                MessageBusMode.InMemory => new InMemoryMessageBus(),
                MessageBusMode.Kafka => new KafkaMessageBus(
                    options.Kafka,
                    loggerFactory.CreateLogger<KafkaMessageBus>()),
                MessageBusMode.K0sStreams => new K0sStreamsMessageBus(
                    serviceProvider.GetRequiredService<IHttpClientFactory>(),
                    options.K0sStreams,
                    loggerFactory.CreateLogger<K0sStreamsMessageBus>()),
                _ => throw new InvalidOperationException($"Unsupported Messaging:Mode '{options.Mode}'.")
            };
        });

        return services;
    }
}
