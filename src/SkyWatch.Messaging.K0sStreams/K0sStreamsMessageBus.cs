using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using SkyWatch.Contracts;
using SkyWatch.Messaging;

namespace SkyWatch.Messaging.K0sStreams;

/// <summary>K0sStreams HTTP API implementation of the broker-neutral message bus.</summary>
public sealed partial class K0sStreamsMessageBus : IMessageBus
{
    public const string HttpClientName = "k0sstreams";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly K0sStreamsBusOptions _options;
    private readonly ILogger<K0sStreamsMessageBus> _logger;
    private readonly string _configuredBaseUrl;
    private string _baseUrl;

    public K0sStreamsMessageBus(
        IHttpClientFactory httpClientFactory,
        K0sStreamsBusOptions options,
        ILogger<K0sStreamsMessageBus> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
        _configuredBaseUrl = options.BaseUrl.TrimEnd('/');
        _baseUrl = _configuredBaseUrl;
    }

    public async Task PublishAsync(string topic, BusRecord record, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentNullException.ThrowIfNull(record);

        if (record.Payload.Length == 0)
        {
            throw new ArgumentException("K0sStreams does not accept empty values.", nameof(record));
        }

        try
        {
            await PublishOnceAsync(topic, record, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "K0sStreams publish failed; resetting the cached leader and retrying once.");
            ResetLeader();
            await PublishOnceAsync(topic, record, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SubscribeAsync(
        string topic,
        string group,
        IMessageHandler handler,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        ArgumentNullException.ThrowIfNull(handler);

        string consumerId = BuildConsumerId(group);
        await JoinAsync(topic, group, consumerId, cancellationToken).ConfigureAwait(false);

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task heartbeat = HeartbeatLoopAsync(topic, group, consumerId, linked.Token);

        try
        {
            while (!linked.IsCancellationRequested)
            {
                ReceiveResponse? delivery = await ReceiveAsync(topic, group, consumerId, linked.Token)
                    .ConfigureAwait(false);

                if (delivery is null)
                {
                    await Task.Delay(_options.PollIntervalMs, linked.Token).ConfigureAwait(false);
                    continue;
                }

                var record = new BusRecord(
                    $"{topic}:{delivery.Partition}:{delivery.Offset}",
                    delivery.Key ?? string.Empty,
                    Encoding.UTF8.GetBytes(delivery.Value),
                    DateTimeOffset.UtcNow);

                var busDelivery = new BusDelivery(
                    topic,
                    delivery.Partition,
                    delivery.Offset,
                    record,
                    delivery.Attempt);

                try
                {
                    MessageResult outcome = await handler
                        .HandleAsync(busDelivery, linked.Token)
                        .ConfigureAwait(false);

                    if (outcome == MessageResult.DeadLetter)
                    {
                        await PublishAsync(Topics.DeadLetter, record, linked.Token).ConfigureAwait(false);
                    }

                    await AckAsync(topic, group, delivery.Partition, delivery.Offset, linked.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (linked.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Handler failed for {Topic}/{Partition}/{Offset} (attempt {Attempt}).",
                        topic,
                        delivery.Partition,
                        delivery.Offset,
                        delivery.Attempt);

                    await NackAsync(topic, group, delivery.Partition, delivery.Offset, ex.Message, linked.Token)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        finally
        {
            linked.Cancel();

            try
            {
                await heartbeat.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }

            await LeaveAsync(topic, group, consumerId, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task PublishOnceAsync(string topic, BusRecord record, CancellationToken cancellationToken)
    {
        var body = new PublishRequest(record.Key, Encoding.UTF8.GetString(record.Payload));
        string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/messages";

        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, path, body, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "publish", cancellationToken).ConfigureAwait(false);
    }

    private async Task<JoinResponse> JoinAsync(
        string topic,
        string group,
        string consumerId,
        CancellationToken cancellationToken)
    {
        string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/queues/{Uri.EscapeDataString(group)}/join";

        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Post,
                path,
                new ConsumerRequest(consumerId),
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "join", cancellationToken).ConfigureAwait(false);

        JoinResponse? result = await response.Content
            .ReadFromJsonAsync<JoinResponse>(SkyWatchJson.Options, cancellationToken)
            .ConfigureAwait(false);

        if (result is null)
        {
            throw new InvalidOperationException("K0sStreams returned an empty join response.");
        }

        _logger.LogInformation(
            "Joined K0sStreams queue {Queue} on {Topic} as {ConsumerId} with partitions {Partitions}.",
            group,
            topic,
            consumerId,
            string.Join(',', result.Partitions));

        return result;
    }

    private async Task<ReceiveResponse?> ReceiveAsync(
        string topic,
        string group,
        string consumerId,
        CancellationToken cancellationToken)
    {
        string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/queues/{Uri.EscapeDataString(group)}/receive";

        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Post,
                path,
                new ReceiveRequest(consumerId, _options.VisibilitySeconds),
                cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return null;
        }

        await EnsureSuccessAsync(response, "receive", cancellationToken).ConfigureAwait(false);

        return await response.Content
            .ReadFromJsonAsync<ReceiveResponse>(SkyWatchJson.Options, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task AckAsync(
        string topic,
        string group,
        int partition,
        long offset,
        CancellationToken cancellationToken)
    {
        string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/queues/{Uri.EscapeDataString(group)}/ack/{partition}/{offset}";

        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, path, null, cancellationToken)
            .ConfigureAwait(false);
        await EnsureSuccessAsync(response, "ack", cancellationToken).ConfigureAwait(false);
    }

    private async Task NackAsync(
        string topic,
        string group,
        int partition,
        long offset,
        string reason,
        CancellationToken cancellationToken)
    {
        string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/queues/{Uri.EscapeDataString(group)}/nack/{partition}/{offset}";

        using HttpResponseMessage response = await SendAsync(
                HttpMethod.Post,
                path,
                new NackRequest(reason),
                cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, "nack", cancellationToken).ConfigureAwait(false);
    }

    private async Task LeaveAsync(
        string topic,
        string group,
        string consumerId,
        CancellationToken cancellationToken)
    {
        try
        {
            string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/queues/{Uri.EscapeDataString(group)}/leave";

            using HttpResponseMessage response = await SendAsync(
                    HttpMethod.Post,
                    path,
                    new ConsumerRequest(consumerId),
                    cancellationToken)
                .ConfigureAwait(false);

            await EnsureSuccessAsync(response, "leave", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not leave K0sStreams queue {Queue} on {Topic}.", group, topic);
        }
    }

    private async Task HeartbeatLoopAsync(
        string topic,
        string group,
        string consumerId,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, _options.HeartbeatSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    string path = $"/v1/topics/{Uri.EscapeDataString(topic)}/queues/{Uri.EscapeDataString(group)}/heartbeat";

                    using HttpResponseMessage response = await SendAsync(
                            HttpMethod.Post,
                            path,
                            new ConsumerRequest(consumerId),
                            cancellationToken)
                        .ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        _logger.LogWarning("K0sStreams lost queue membership for {ConsumerId}; rejoining.", consumerId);
                        await JoinAsync(topic, group, consumerId, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    await EnsureSuccessAsync(response, "heartbeat", cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "K0sStreams heartbeat failed for {ConsumerId}.", consumerId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown.
        }
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string path,
        object? body,
        CancellationToken cancellationToken)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        var request = new HttpRequestMessage(method, new Uri(new Uri(_baseUrl + "/"), path.TrimStart('/')));

        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: SkyWatchJson.Options);
        }

        HttpResponseMessage response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.RequestMessage?.RequestUri is { } finalUri)
        {
            _baseUrl = finalUri.GetLeftPart(UriPartial.Authority);
        }

        return response;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string operation,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new HttpRequestException(
            $"K0sStreams {operation} failed with status {(int)response.StatusCode} ({response.StatusCode}): {body}");
    }

    private void ResetLeader() => _baseUrl = _configuredBaseUrl;

    private static string BuildConsumerId(string group)
    {
        string safeGroup = InvalidConsumerCharacters().Replace(group, "-");
        string safeMachine = InvalidConsumerCharacters().Replace(Environment.MachineName, "-");
        return $"{safeGroup}-{safeMachine}-{Guid.NewGuid():N}";
    }

    [GeneratedRegex("[^a-zA-Z0-9_.-]", RegexOptions.CultureInvariant)]
    private static partial Regex InvalidConsumerCharacters();

    private sealed record PublishRequest(string Key, string Value);

    private sealed record ConsumerRequest(string ConsumerId);

    private sealed record ReceiveRequest(string ConsumerId, int VisibilitySeconds);

    private sealed record NackRequest(string? Reason);

    private sealed record JoinResponse(string ConsumerId, int[] Partitions, long Generation);

    private sealed record ReceiveResponse(
        int Partition,
        long Offset,
        string? Key,
        string Value,
        int Attempt,
        DateTimeOffset VisibleUntil);
}
