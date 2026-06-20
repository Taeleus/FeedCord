using System.Net;
using System.Text.Json;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace FeedCord.Infrastructure.Http;

public sealed class CustomHttpClient : ICustomHttpClient
{
    private const string FeedCordUserAgent = "FeedCord/3.1 (+https://github.com/Taeleus/FeedCord)";
    private readonly HttpClient _innerClient;
    private readonly ILogger<CustomHttpClient> _logger;
    private readonly SemaphoreSlim _throttle;

    public CustomHttpClient(
        ILogger<CustomHttpClient> logger,
        HttpClient innerClient,
        SemaphoreSlim throttle)
    {
        _logger = logger;
        _throttle = throttle;
        _innerClient = innerClient;
    }

    public async Task<HttpResponseMessage?> GetAsyncWithFallback(
        string url,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await SendGetAsync(url, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogWarning(
                "Request timed out for {Url}: {ErrorType}",
                SafeUrl(url),
                ex.GetType().Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "GET request failed for {Url}: {ErrorType}",
                SafeUrl(url),
                ex.GetType().Name);
        }

        return null;
    }

    public async Task<bool> PostAsyncWithFallback(
        string url,
        StringContent forumChannelContent,
        StringContent textChannelContent,
        bool isForum,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await SendPostWithRateLimitAsync(
                url,
                isForum ? forumChannelContent : textChannelContent,
                cancellationToken);

            if (response.IsSuccessStatusCode)
                return true;

            var responseError = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError(
                "Discord returned HTTP {StatusCode}: {ResponseError}",
                response.StatusCode,
                responseError);

            if (response.StatusCode != HttpStatusCode.BadRequest)
                return false;

            using var fallbackResponse = await SendPostWithRateLimitAsync(
                url,
                isForum ? textChannelContent : forumChannelContent,
                cancellationToken);

            if (fallbackResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Discord accepted the alternate channel payload; correct the Forum setting");
                return true;
            }

            _logger.LogError(
                "Discord fallback returned HTTP {StatusCode}: {ResponseError}",
                fallbackResponse.StatusCode,
                await fallbackResponse.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Failed to post to Discord webhook {Webhook}: {ErrorType}",
                SafeUrl(url),
                ex.GetType().Name);
        }

        return false;
    }

    private async Task<HttpResponseMessage> SendGetAsync(
        string url,
        CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd(FeedCordUserAgent);
            request.Headers.Accept.ParseAdd(
                "application/rss+xml, application/atom+xml, application/xml, text/xml, text/html;q=0.8, */*;q=0.5");
            return await _innerClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<HttpResponseMessage> SendPostAsync(
        string url,
        StringContent content,
        CancellationToken cancellationToken)
    {
        await _throttle.WaitAsync(cancellationToken);
        try
        {
            return await _innerClient.PostAsync(url, content, cancellationToken);
        }
        finally
        {
            _throttle.Release();
        }
    }

    private async Task<HttpResponseMessage> SendPostWithRateLimitAsync(
        string url,
        StringContent content,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var response = await SendPostAsync(url, content, cancellationToken);
            if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == maxAttempts)
                return response;

            var delay = await GetRetryDelayAsync(response, cancellationToken);
            response.Dispose();

            _logger.LogWarning(
                "Discord rate limited the webhook; retrying in {DelayMs} ms (attempt {Attempt}/{MaxAttempts})",
                delay.TotalMilliseconds,
                attempt + 1,
                maxAttempts);

            await Task.Delay(delay, cancellationToken);
        }

        throw new InvalidOperationException("Discord retry loop exited unexpectedly.");
    }

    private static async Task<TimeSpan> GetRetryDelayAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Headers.RetryAfter?.Delta is { } headerDelay)
            return ClampRetryDelay(headerDelay);

        try
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("retry_after", out var retryAfter) &&
                retryAfter.TryGetDouble(out var seconds))
            {
                return ClampRetryDelay(TimeSpan.FromSeconds(seconds));
            }
        }
        catch (JsonException)
        {
            // Use the conservative fallback below for malformed rate-limit responses.
        }

        return TimeSpan.FromSeconds(1);
    }

    private static TimeSpan ClampRetryDelay(TimeSpan delay)
    {
        return TimeSpan.FromMilliseconds(Math.Clamp(delay.TotalMilliseconds, 250, 60_000));
    }

    public static string SafeUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return "<invalid-url>";

        if (uri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
            return $"{uri.Scheme}://{uri.Host}/api/webhooks/[redacted]";

        return uri.GetLeftPart(UriPartial.Path);
    }
}
