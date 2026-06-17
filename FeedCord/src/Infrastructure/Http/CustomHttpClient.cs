using System.Net;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using FeedCord.Services.Interfaces;
using System.Collections.Concurrent;

namespace FeedCord.Infrastructure.Http
{
    public class CustomHttpClient : ICustomHttpClient
    {
        private const string USER_MIMICK = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 " +
                                          "(KHTML, like Gecko) Chrome/104.0.5112.79 Safari/537.36";
        private const string GOOGLE_FEED_FETCHER = "FeedFetcher-Google";

        private readonly HttpClient _innerClient;
        private readonly ILogger<CustomHttpClient> _logger;
        private readonly SemaphoreSlim _throttle;
        private readonly ConcurrentDictionary<string, string> _userAgentCache;

        public CustomHttpClient(ILogger<CustomHttpClient> logger, HttpClient innerClient, SemaphoreSlim throttle)
        {
            _logger = logger;
            _throttle = throttle;
            _innerClient = innerClient;
            _userAgentCache = new ConcurrentDictionary<string, string>();
        }

        public async Task<HttpResponseMessage?> GetAsyncWithFallback(string url)
        {
            HttpResponseMessage? response = null;

            try
            {
                response = await SendGetAsync(url, _userAgentCache.GetValueOrDefault(url));

                if (response.IsSuccessStatusCode)
                    return response;

                response = await TryAlternativeAsync(url, response);

                return response;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogWarning("Request to {Url} was canceled: {Ex}", url, ex);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning("Operation was canceled for {Url}: {Ex}", url, ex);
            }
            catch (Exception ex)
            {
                _logger.LogError("An error occurred while processing the request for {Url}: {Ex}", url, ex);
            }

            return response;
        }

        public async Task PostAsyncWithFallback(string url, StringContent forumChannelContent, StringContent textChannelContent, bool isForum)
        {
            HttpResponseMessage response;

            try
            {
                response = await SendPostAsync(url, isForum ? forumChannelContent : textChannelContent);

                if (response.StatusCode == HttpStatusCode.NoContent)
                    return;

                _logger.LogError("Response Error: {ResponseError}", await response.Content.ReadAsStringAsync());

                response = await SendPostAsync(url, !isForum ? forumChannelContent : textChannelContent);

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    _logger.LogWarning(
                        "Successfully posted to Discord Channel after switching channel type - Change Forum Property in Config!!");
                }
                else
                {
                    _logger.LogError("Failed to post to Discord Channel after fallback attempts");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to post to Discord Channel: {Url}", url);
            }
        }

        private async Task<HttpResponseMessage> TryAlternativeAsync(string url, HttpResponseMessage oldResponse)
        {
            try
            {
                var response = await SendGetAsync(url, USER_MIMICK);

                if (response.IsSuccessStatusCode)
                {
                    _userAgentCache.AddOrUpdate(url, USER_MIMICK, (_, _) => USER_MIMICK);
                    return response;
                }

                response = await SendGetAsync(url, GOOGLE_FEED_FETCHER);

                if (response.IsSuccessStatusCode)
                {
                    _userAgentCache.AddOrUpdate(url, GOOGLE_FEED_FETCHER, (_, _) => GOOGLE_FEED_FETCHER);
                    return response;
                }

                var uri = new Uri(url);
                var baseUrl = uri.GetLeftPart(UriPartial.Authority);
                var robotsUrl = new Uri(new Uri(baseUrl), "/robots.txt").AbsoluteUri;
                var userAgents = await GetRobotsUserAgentsAsync(robotsUrl);

                foreach (var userAgent in userAgents)
                {
                    response = await SendGetAsync(url, userAgent, acceptAny: true);

                    if (response.IsSuccessStatusCode)
                    {
                        _userAgentCache.AddOrUpdate(url, userAgent, (_, _) => userAgent);
                        return response;
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogError("Failed to fetch RSS Feed after fallback attempts: {Url} - {E}", url, e);
            }

            return oldResponse;
        }

        private async Task<HttpResponseMessage> SendGetAsync(string url, string? userAgent = null, bool acceptAny = false)
        {
            await _throttle.WaitAsync();

            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, url);

                if (!string.IsNullOrWhiteSpace(userAgent))
                    request.Headers.UserAgent.ParseAdd(userAgent);

                if (acceptAny)
                    request.Headers.Add("Accept", "*/*");

                return await _innerClient.SendAsync(request);
            }
            finally
            {
                _throttle.Release();
            }
        }

        private async Task<HttpResponseMessage> SendPostAsync(string url, StringContent content)
        {
            await _throttle.WaitAsync();

            try
            {
                return await _innerClient.PostAsync(url, content);
            }
            finally
            {
                _throttle.Release();
            }
        }

        private async Task<string> FetchRobotsContentAsync(string url)
        {
            try
            {
                var response = await SendGetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return string.Empty;

                return await response.Content.ReadAsStringAsync();
            }
            catch
            {
                return string.Empty;
            }
        }

        private async Task<List<string>> GetRobotsUserAgentsAsync(string url)
        {
            var userAgents = new List<string>();

            var robotsContent = await FetchRobotsContentAsync(url);

            if (robotsContent == string.Empty)
                return userAgents.OrderByDescending(x => x).Distinct().ToList();

            var pattern = @"User-agent:\s*(?<agent>.+)";
            var regex = new Regex(pattern);

            var matches = regex.Matches(robotsContent);

            foreach (Match match in matches)
            {
                var userAgent = match.Groups["agent"].Value.Trim();

                if (!string.IsNullOrEmpty(userAgent))
                    userAgents.Add(userAgent);
            }

            return userAgents.OrderByDescending(x => x).Distinct().ToList();
        }
    }
}