using FeedCord.Common;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using FeedCord.Core.Interfaces;
using FeedCord.Services.Helpers;
using FeedCord.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace FeedCord.Services
{
    public class FeedManager : IFeedManager
    {
        private readonly Config _config;
        private readonly SemaphoreSlim _instancedConcurrentRequests;
        private readonly ICustomHttpClient _httpClient;
        private readonly ILogAggregator _logAggregator;
        private readonly ILogger<FeedManager> _logger;
        private readonly IRssParsingService _rssParsingService;
        private readonly IPostFilterService _postFilterService;
        private readonly IFeedStateStore _stateStore;
        private IReadOnlyDictionary<string, ReferencePost> _lastRunReference;
        private readonly ConcurrentDictionary<string, FeedState> _feedStates;

        [ActivatorUtilitiesConstructor]
        public FeedManager(
            Config config,
            ICustomHttpClient httpClient,
            IRssParsingService rssParsingService,
            IPostFilterService postFilterService,
            IFeedStateStore stateStore,
            ILogger<FeedManager> logger,
            ILogAggregator logAggregator)
        {
            _config = config;
            _httpClient = httpClient;
            _stateStore = stateStore;
            _lastRunReference = new Dictionary<string, ReferencePost>();
            _rssParsingService = rssParsingService;
            _postFilterService = postFilterService;
            _logger = logger;
            _logAggregator = logAggregator;
            _feedStates = new ConcurrentDictionary<string, FeedState>();
            _instancedConcurrentRequests = new SemaphoreSlim(config.ConcurrentRequests);
        }

        public FeedManager(
            Config config,
            ICustomHttpClient httpClient,
            IRssParsingService rssParsingService,
            IPostFilterService postFilterService,
            ILogger<FeedManager> logger,
            ILogAggregator logAggregator)
            : this(
                config,
                httpClient,
                rssParsingService,
                postFilterService,
                NullFeedStateStore.Instance,
                logger,
                logAggregator)
        {
        }
        public async Task<List<PendingPost>> CheckForNewPostsAsync(
            CancellationToken cancellationToken = default)
        {
            ConcurrentBag<PendingPost> allNewPosts = new();

            var tasks = _feedStates.Select(async (feed) =>
                await CheckSingleFeedAsync(
                    feed.Key,
                    feed.Value,
                    allNewPosts,
                    _config.DescriptionLimit,
                    cancellationToken));

            await Task.WhenAll(tasks);

            _logAggregator.SetNewPostCount(allNewPosts.Count(post => post.ShouldNotify));

            return allNewPosts
                .OrderBy(item => item.Post.PublishDate)
                .ThenBy(item => PostIdentity.GetStableId(item.Post), StringComparer.Ordinal)
                .ToList();
        }

        public async Task AcknowledgePostsAsync(
            IReadOnlyCollection<PendingPost> pendingPosts,
            CancellationToken cancellationToken = default)
        {
            if (pendingPosts.Count == 0)
                return;

            var feedUrl = pendingPosts.First().FeedUrl;
            if (!_feedStates.TryGetValue(feedUrl, out var feedState))
                return;

            var latestDate = pendingPosts.Max(item => item.Post.PublishDate).ToUniversalTime();
            if (latestDate > feedState.LastPublishDate)
            {
                feedState.LastPublishDate = latestDate;
                feedState.ItemIdsAtLastPublishDate = pendingPosts
                    .Where(item => item.Post.PublishDate.ToUniversalTime() == latestDate)
                    .Select(item => PostIdentity.GetStableId(item.Post))
                    .ToHashSet(StringComparer.Ordinal);
            }
            else if (latestDate == feedState.LastPublishDate)
            {
                foreach (var pendingPost in pendingPosts.Where(
                             item => item.Post.PublishDate.ToUniversalTime() == latestDate))
                {
                    feedState.ItemIdsAtLastPublishDate.Add(PostIdentity.GetStableId(pendingPost.Post));
                }
            }

            feedState.ErrorCount = 0;

            if (_config.PersistenceOnShutdown)
                await SaveStateAsync(cancellationToken);
        }

        public Task SaveStateAsync(CancellationToken cancellationToken = default)
        {
            return _stateStore.SaveAsync(_config.Id, _feedStates, cancellationToken);
        }
        public async Task InitializeUrlsAsync(CancellationToken cancellationToken = default)
        {
            _lastRunReference = await _stateStore.LoadAsync(_config.Id, cancellationToken);

            var id = _config.Id;
            var validRssUrls = _config.RssUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToArray();

            var validYoutubeUrls = _config.YoutubeUrls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .ToArray();

            var rssCount = await GetSuccessCount(validRssUrls, false, cancellationToken);
            var youtubeCount = await GetSuccessCount(validYoutubeUrls, true, cancellationToken);
            var successCount = rssCount + youtubeCount;

            var totalUrls = validRssUrls.Length + validYoutubeUrls.Length;

            _logger.LogInformation("{id}: Tested successfully for {UrlCount} out of {TotalUrls} Urls in Configuration File", id, successCount, totalUrls);
        }

        public IReadOnlyDictionary<string, FeedState> GetAllFeedData()
        {
            return _feedStates;
        }
        private async Task<int> GetSuccessCount(
            string[] urls,
            bool isYoutube,
            CancellationToken cancellationToken)
        {
            var successCount = 0;

            if (urls.Length == 0 || urls.Length == 1 && string.IsNullOrEmpty(urls[0]))
            {
                return successCount;
            }

            foreach (var url in urls)
            {
                var isSuccess = await TestUrlAsync(url, cancellationToken);

                if (!isSuccess)
                {
                    continue;
                }

                if (_lastRunReference.TryGetValue(url, out var value))
                {
                    _feedStates.TryAdd(url, new FeedState
                    {
                        IsYoutube = isYoutube,
                        LastPublishDate = value.LastRunDate.ToUniversalTime(),
                        ItemIdsAtLastPublishDate = new HashSet<string>(
                            value.ItemIdsAtLastRunDate,
                            StringComparer.Ordinal),
                        ErrorCount = 0
                    });

                    successCount++;

                    continue;
                }

                bool successfulAdd;

                if (isYoutube)
                {
                    successfulAdd = _feedStates.TryAdd(url, new FeedState
                    {
                        IsYoutube = true,
                        LastPublishDate = DateTimeOffset.UtcNow,
                        ErrorCount = 0
                    });
                }
                else
                {
                    successfulAdd = _feedStates.TryAdd(url, new FeedState
                    {
                        IsYoutube = false,
                        LastPublishDate = DateTimeOffset.UtcNow,
                        ErrorCount = 0
                    });
                }

                if (successfulAdd)
                {
                    successCount++;
                }

                else
                {
                    _logger.LogWarning("Failed to initialize URL: {Url}", url);
                }
            }

            return successCount;
        }
        private async Task<bool> TestUrlAsync(string url, CancellationToken cancellationToken)
        {
            var acquired = false;
            try
            {
                await _instancedConcurrentRequests.WaitAsync(cancellationToken);
                acquired = true;

                using var response = await _httpClient.GetAsyncWithFallback(url, cancellationToken);

                if (response is null)
                {
                    _logAggregator.AddUrlResponse(url, -99);
                    return false;
                }

                _logAggregator.AddUrlResponse(url, (int)response.StatusCode);

                response.EnsureSuccessStatusCode();

                return true;
            }
            catch (HttpRequestException ex)
            {
                _logAggregator.AddUrlResponse(url, (int)(ex.StatusCode ?? System.Net.HttpStatusCode.BadRequest));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                _logger.LogWarning("Failed to instantiate URL: {Url}", url);
            }
            finally
            {
                if (acquired)
                    _instancedConcurrentRequests.Release();
            }

            return false;
        }
        private async Task CheckSingleFeedAsync(
            string url,
            FeedState feedState,
            ConcurrentBag<PendingPost> newPosts,
            int trim,
            CancellationToken cancellationToken)
        {
            List<Post?> posts;
            var acquired = false;

            try
            {
                await _instancedConcurrentRequests.WaitAsync(cancellationToken);
                acquired = true;

                posts = feedState.IsYoutube ?
                    await FetchYoutubeAsync(url, cancellationToken) :
                    await FetchRssAsync(url, trim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                HandleFeedError(url, feedState, ex);
                return;
            }
            finally
            {
                if (acquired)
                    _instancedConcurrentRequests.Release();
            }

            feedState.ErrorCount = 0;

            var freshlyFetched = posts
                .Where(p => p is not null && IsUnseen(p, feedState))
                .OrderBy(p => p!.PublishDate)
                .ToList();

            if (freshlyFetched.Any())
            {
                foreach (var post in freshlyFetched)
                {
                    if (post is null)
                    {
                        _logger.LogWarning("Failed to parse a post from {Url}", url);
                        continue;
                    }

                    var shouldNotify = _postFilterService.ShouldInclude(post, url);
                    newPosts.Add(new PendingPost(url, post, shouldNotify));

                    if (!shouldNotify)
                    {
                        _logger.LogInformation(
                            "A new post was omitted because it does not comply with configured filters: {Url}", url);
                    }
                }
            }
            else
            {
                _logAggregator.AddLatestUrlPost(url, posts.OrderByDescending(p => p?.PublishDate).FirstOrDefault());
            }

        }
        private async Task<List<Post?>> FetchYoutubeAsync(
            string url,
            CancellationToken cancellationToken)
        {
            // Treat YouTube URLs with embedded xml directly as a feed source.
            if (IsYoutubeFeedUrl(url))
            {
                return await _rssParsingService.ParseYoutubeFeedAsync(url, cancellationToken);
            }

            using var response = await _httpClient.GetAsyncWithFallback(url, cancellationToken);

            if (response is null)
            {
                throw new HttpRequestException($"No response from {url}");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("YouTube URL returned HTTP {StatusCode}: {Url}", response.StatusCode, url);
                throw new HttpRequestException($"HTTP {response.StatusCode}", null, response.StatusCode);
            }

            var xmlContent = await GetResponseContentAsync(response, cancellationToken);
            return await _rssParsingService.ParseYoutubeFeedAsync(xmlContent, cancellationToken);
        }

        private async Task<List<Post?>> FetchRssAsync(
            string url,
            int trim,
            CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsyncWithFallback(url, cancellationToken);

            if (response is null)
            {
                throw new HttpRequestException($"No response from {url}");
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("RSS URL returned HTTP {StatusCode}: {Url}", response.StatusCode, url);
                throw new HttpRequestException($"HTTP {response.StatusCode}", null, response.StatusCode);
            }

            var xmlContent = await GetResponseContentAsync(response, cancellationToken);
            return await _rssParsingService.ParseRssFeedAsync(xmlContent, trim, cancellationToken);
        }

        private async Task<string> GetResponseContentAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            try
            {
                if (response.Content.Headers.ContentEncoding.Contains("gzip"))
                {
                    await using var decompressedStream = new GZipStream(
                        await response.Content.ReadAsStreamAsync(cancellationToken),
                        CompressionMode.Decompress);
                    using var reader = new StreamReader(decompressedStream, Encoding.UTF8);
                    return await reader.ReadToEndAsync(cancellationToken);
                }
                else
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    return EncodingExtractor.ConvertBytesByComparing(bytes, response.Content.Headers);
                }
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Failed to decode response content from {Url}", response.RequestMessage?.RequestUri);
                return string.Empty;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read response content from {Url}", response.RequestMessage?.RequestUri);
                return string.Empty;
            }
        }

        private void HandleFeedError(string url, FeedState feedState, Exception ex)
        {
            feedState.ErrorCount++;
            _logger.LogError(ex, "Failed to fetch feed from {Url}. Error count: {ErrorCount}", url, feedState.ErrorCount);

            if (feedState.ErrorCount < 3 || !_config.EnableAutoRemove) return;

            _logger.LogWarning("Removing Url: {Url} after too many errors", url);
            var successRemove = _feedStates.TryRemove(url, out _);

            if (!successRemove)
            {
                _logger.LogWarning("Failed to remove Url: {Url}", url);
            }
        }

        private static bool IsYoutubeFeedUrl(string url)
        {
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                   (uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    uri.AbsolutePath.Equals("/feeds/videos.xml", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsUnseen(Post post, FeedState feedState)
        {
            var publishDate = post.PublishDate.ToUniversalTime();
            if (publishDate > feedState.LastPublishDate)
                return true;

            return publishDate == feedState.LastPublishDate &&
                   !feedState.ItemIdsAtLastPublishDate.Contains(PostIdentity.GetStableId(post));
        }

    }
}
