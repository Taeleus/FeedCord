using System.Net;
using System.Net.Http.Headers;
using FeedCord.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FeedCord.Tests;

public class CustomHttpClientTests
{
    [Fact]
    public async Task PostAsyncWithFallback_RetriesDiscordRateLimit()
    {
        var handler = new SequenceHandler(
            new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("""{"retry_after":0}""")
            },
            new HttpResponseMessage(HttpStatusCode.NoContent));
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(handler),
            new SemaphoreSlim(1));

        using var forum = new StringContent("{}");
        using var text = new StringContent("{}");
        var result = await client.PostAsyncWithFallback(
            "https://discord.com/api/webhooks/1/token",
            forum,
            text,
            false);

        Assert.True(result);
        Assert.Equal(2, handler.RequestCount);
    }

    [Fact]
    public async Task PostAsyncWithFallback_ReturnsFalse_OnNonRecoverableFailure()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent("unauthorized")
        });
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(handler),
            new SemaphoreSlim(1));

        using var forum = new StringContent("{}");
        using var text = new StringContent("{}");
        var result = await client.PostAsyncWithFallback(
            "https://discord.com/api/webhooks/1/token",
            forum,
            text,
            false);

        Assert.False(result);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetAsyncWithFallback_UsesHonestFeedCordUserAgent()
    {
        var handler = new CapturingHandler();
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(handler),
            new SemaphoreSlim(1));

        using var response = await client.GetAsyncWithFallback("https://example.com/rss");

        Assert.NotNull(response);
        Assert.StartsWith("FeedCord/", handler.UserAgent);
        Assert.DoesNotContain("Mozilla", handler.UserAgent);
        Assert.DoesNotContain("Google", handler.UserAgent);
    }

    [Fact]
    public async Task GetAsyncWithFallback_PropagatesCallerCancellation()
    {
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(new BlockingHandler()),
            new SemaphoreSlim(1));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.GetAsyncWithFallback("https://example.com/rss", cancellation.Token));
    }

    [Fact]
    public void SafeUrl_RedactsDiscordWebhookCredentials()
    {
        var safe = CustomHttpClient.SafeUrl(
            "https://discord.com/api/webhooks/123456/secret-token");

        Assert.Equal("https://discord.com/api/webhooks/[redacted]", safe);
        Assert.DoesNotContain("123456", safe);
        Assert.DoesNotContain("secret-token", safe);
    }

    [Fact]
    public async Task GetAsyncWithFallback_RejectsResponseAboveMaximumSize()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(new byte[11])
        });
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(handler),
            new SemaphoreSlim(1),
            maxResponseBytes: 10);

        using var response = await client.GetAsyncWithFallback("https://example.com/rss");

        Assert.Null(response);
    }

    [Fact]
    public async Task GetAsyncWithFallback_TimesOutStalledResponseBody()
    {
        var handler = new SequenceHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StalledContent()
        });
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(handler),
            new SemaphoreSlim(1),
            responseBodyTimeout: TimeSpan.FromMilliseconds(50));

        using var response = await client.GetAsyncWithFallback("https://example.com/rss");

        Assert.Null(response);
    }

    [Fact]
    public async Task GetPublicAsync_BlocksRedirectToPrivateNetwork()
    {
        var redirect = new HttpResponseMessage(HttpStatusCode.Redirect);
        redirect.Headers.Location = new Uri("http://127.0.0.1/admin");
        var handler = new SequenceHandler(redirect);
        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient(handler),
            new SemaphoreSlim(1));

        using var response = await client.GetPublicAsync("http://93.184.216.34/image");

        Assert.Null(response);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(_responses.Dequeue());
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string UserAgent { get; private set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            UserAgent = request.Headers.UserAgent.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class StalledContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan);
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            System.Net.TransportContext? context,
            CancellationToken cancellationToken)
        {
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
