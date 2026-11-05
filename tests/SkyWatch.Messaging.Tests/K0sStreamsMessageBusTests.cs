using Xunit;
using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using SkyWatch.Messaging;
using SkyWatch.Messaging.K0sStreams;

namespace SkyWatch.Messaging.Tests;

public sealed class K0sStreamsMessageBusTests
{
    [Fact]
    public async Task Publish_posts_key_and_value()
    {
        var handler = new FakeK0sStreamsHandler();
        var bus = CreateBus(handler);

        await bus.PublishAsync(
            "flight-positions",
            new BusRecord("id", "abc123", Encoding.UTF8.GetBytes("{\"hex\":\"abc123\"}"), DateTimeOffset.UtcNow),
            CancellationToken.None);

        FakeRequest request = Assert.Single(handler.Requests, item => item.Path.EndsWith("/messages", StringComparison.Ordinal));
        Assert.Equal(HttpMethod.Post, request.Method);

        using JsonDocument document = JsonDocument.Parse(request.Body);
        Assert.Equal("abc123", document.RootElement.GetProperty("key").GetString());
        Assert.Equal("{\"hex\":\"abc123\"}", document.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task Subscribe_joins_receives_acks_and_leaves()
    {
        var handler = new FakeK0sStreamsHandler();
        var bus = CreateBus(handler);
        var delivered = new TaskCompletionSource<BusDelivery>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Task subscription = bus.SubscribeAsync(
            "flight-positions",
            "skywatch-tracker",
            new DelegateMessageHandler((delivery, _) =>
            {
                delivered.TrySetResult(delivery);
                return Task.FromResult(MessageResult.Ack);
            }),
            cancellation.Token);

        BusDelivery result = await delivered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(7, result.Offset);
        Assert.Equal("abc123", result.Record.Key);
        Assert.Equal("{\"hex\":\"abc123\"}", Encoding.UTF8.GetString(result.Record.Payload));

        await cancellation.CancelAsync();
        await subscription;

        Assert.Contains(handler.Requests, request => request.Path.EndsWith("/join", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Path.Contains("/ack/", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.Path.EndsWith("/leave", StringComparison.Ordinal));
    }

    private static K0sStreamsMessageBus CreateBus(FakeK0sStreamsHandler handler)
    {
        var options = new K0sStreamsBusOptions
        {
            BaseUrl = "http://k0sstreams.test",
            VisibilitySeconds = 30,
            HeartbeatSeconds = 30,
            PollIntervalMs = 10
        };

        var factory = new TestHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("http://k0sstreams.test") });
        return new K0sStreamsMessageBus(factory, options, NullLogger<K0sStreamsMessageBus>.Instance);
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FakeK0sStreamsHandler : HttpMessageHandler
    {
        private int _receiveCount;

        public ConcurrentQueue<FakeRequest> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            Requests.Enqueue(new FakeRequest(request.Method, request.RequestUri!.AbsolutePath, body));

            if (request.RequestUri.AbsolutePath.EndsWith("/join", StringComparison.Ordinal))
            {
                return Json("{\"consumerId\":\"c1\",\"partitions\":[0,1,2],\"generation\":1}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/receive", StringComparison.Ordinal))
            {
                if (Interlocked.Increment(ref _receiveCount) == 1)
                {
                    return Json("{\"partition\":0,\"offset\":7,\"key\":\"abc123\",\"value\":\"{\\\"hex\\\":\\\"abc123\\\"}\",\"attempt\":1,\"visibleUntil\":\"2030-01-01T00:00:00Z\"}");
                }

                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            if (request.RequestUri.AbsolutePath.Contains("/ack/", StringComparison.Ordinal))
            {
                return Json("{\"topic\":\"flight-positions\"}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/heartbeat", StringComparison.Ordinal) ||
                request.RequestUri.AbsolutePath.EndsWith("/leave", StringComparison.Ordinal))
            {
                return Json("{}");
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/messages", StringComparison.Ordinal))
            {
                return Json("{\"partition\":0,\"offset\":1}");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage Json(string json) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
    }

    private sealed record FakeRequest(HttpMethod Method, string Path, string Body);
}
