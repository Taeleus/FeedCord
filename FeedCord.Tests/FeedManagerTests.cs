using System.Collections.Concurrent;
using FeedCord.Common;
using FeedCord.Services;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FeedCord.Tests
{
    public class FeedManagerTests
    {
        [Fact]
        public void PostFilterService_ShouldInclude_WhenNoFiltersConfigured_ReturnsTrue()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = Array.Empty<string>(),
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = null
            };

            var service = new PostFilterService(config);
            var post = new Post("Title", "image", "description", "link", "tag", DateTimeOffset.UtcNow, "author", Array.Empty<string>());

            Assert.True(service.ShouldInclude(post, "https://example.com/rss"));
        }

        [Fact]
        public void PostFilterService_ShouldInclude_WhenExactFilterMatches_ReturnsTrue()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = Array.Empty<string>(),
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = new List<PostFilters>
                {
                    new PostFilters
                    {
                        Url = "https://example.com/rss",
                        Filters = new[] { "label:tag" }
                    }
                }
            };

            var service = new PostFilterService(config);
            var post = new Post("Title", "image", "description", "link", "tag", DateTimeOffset.UtcNow, "author", new[] { "tag" });

            Assert.True(service.ShouldInclude(post, "https://example.com/rss"));
        }

        [Fact]
        public void PostFilterService_ShouldInclude_WhenAllFilterMatchesAndExactDoesNotExist_ReturnsTrue()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = Array.Empty<string>(),
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = new List<PostFilters>
                {
                    new PostFilters
                    {
                        Url = "all",
                        Filters = new[] { "Title" }
                    }
                }
            };

            var service = new PostFilterService(config);
            var post = new Post("Title", "image", "description", "link", "tag", DateTimeOffset.UtcNow, "author", Array.Empty<string>());

            Assert.True(service.ShouldInclude(post, "https://example.com/rss"));
        }

        [Fact]
        public async Task InitializeUrlsAsync_ShouldPopulateFeedStates_WhenUrlsAreValid()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = null
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<rss></rss>")
                });

            var rssParsingServiceMock = new Mock<IRssParsingService>();
            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();

            var feedData = feedManager.GetAllFeedData();
            Assert.Single(feedData);
            Assert.True(feedData.ContainsKey("https://example.com/rss"));
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldReturnNewPost_WhenFilterMatches()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = new List<PostFilters>
                {
                    new PostFilters
                    {
                        Url = "https://example.com/rss",
                        Filters = new[] { "label:tag" }
                    }
                }
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<rss></rss>")
                });

            var post = new Post("Title", "image", "description", "https://example.com/post", "tag", DateTimeOffset.UtcNow.AddSeconds(1), "author", new[] { "tag" });
            var rssParsingServiceMock = new Mock<IRssParsingService>();
            rssParsingServiceMock.Setup(x => x.ParseRssFeedAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Post?> { post });

            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();
            var newPosts = await feedManager.CheckForNewPostsAsync();

            Assert.Single(newPosts);
            Assert.Equal(post, newPosts[0].Post);
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldExcludeNewPost_WhenFilterDoesNotMatch()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = new List<PostFilters>
                {
                    new PostFilters
                    {
                        Url = "https://example.com/rss",
                        Filters = new[] { "label:nomatch" }
                    }
                }
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<rss></rss>")
                });

            var post = new Post("Title", "image", "description", "https://example.com/post", "tag", DateTimeOffset.UtcNow.AddSeconds(1), "author", new[] { "tag" });
            var rssParsingServiceMock = new Mock<IRssParsingService>();
            rssParsingServiceMock.Setup(x => x.ParseRssFeedAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Post?> { post });

            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();
            var newPosts = await feedManager.CheckForNewPostsAsync();

            var pendingPost = Assert.Single(newPosts);
            Assert.False(pendingPost.ShouldNotify);
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldReturnEmpty_WhenHttpResponseIsNull()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = null
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((System.Net.Http.HttpResponseMessage?)null);

            var rssParsingServiceMock = new Mock<IRssParsingService>();
            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();
            var newPosts = await feedManager.CheckForNewPostsAsync();

            Assert.Empty(newPosts);
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldReturnEmpty_WhenRssParseFails()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = null
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<rss></rss>")
                });

            var rssParsingServiceMock = new Mock<IRssParsingService>();
            rssParsingServiceMock.Setup(x => x.ParseRssFeedAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new System.Exception("parse failed"));

            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();
            var newPosts = await feedManager.CheckForNewPostsAsync();

            Assert.Empty(newPosts);
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldReturnEmpty_WhenYoutubeParseFails()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = Array.Empty<string>(),
                YoutubeUrls = new[] { "https://example.com/channel" },
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = null
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<rss></rss>")
                });

            var rssParsingServiceMock = new Mock<IRssParsingService>();
            rssParsingServiceMock.Setup(x => x.ParseYoutubeFeedAsync(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Post?>());

            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();
            var newPosts = await feedManager.CheckForNewPostsAsync();

            Assert.Empty(newPosts);
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldReturnNewPost_WhenAllFilterMatches()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                PostFilters = new List<PostFilters>
                {
                    new PostFilters
                    {
                        Url = "all",
                        Filters = new[] { "Title" }
                    }
                }
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            httpClientMock.Setup(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new System.Net.Http.StringContent("<rss></rss>")
                });

            var post = new Post("Title", "image", "description", "https://example.com/post", "tag", DateTimeOffset.UtcNow.AddSeconds(1), "author", new[] { "tag" });
            var rssParsingServiceMock = new Mock<IRssParsingService>();
            rssParsingServiceMock.Setup(x => x.ParseRssFeedAsync(
                    It.IsAny<string>(),
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Post?> { post });

            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();
            var newPosts = await feedManager.CheckForNewPostsAsync();

            Assert.Single(newPosts);
            Assert.Equal(post, newPosts[0].Post);
        }

        [Fact]
        public async Task CheckForNewPostsAsync_ShouldRemoveFeed_WhenAutoRemoveEnabledAfterThreeFailures()
        {
            var config = new Config
            {
                Id = "test",
                RssUrls = new[] { "https://example.com/rss" },
                YoutubeUrls = Array.Empty<string>(),
                DiscordWebhookUrl = "https://example.com",
                RssCheckIntervalMinutes = 1,
                DescriptionLimit = 1,
                Forum = false,
                MarkdownFormat = false,
                PersistState = false,
                EnableAutoRemove = true,
                PostFilters = null
            };

            var httpClientMock = new Mock<ICustomHttpClient>();
            var okResponse = new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent("<rss></rss>")
            };

            httpClientMock.SetupSequence(x => x.GetAsyncWithFallback(
                    It.IsAny<string>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(okResponse)
                .ThrowsAsync(new System.Net.Http.HttpRequestException("failed"))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("failed"))
                .ThrowsAsync(new System.Net.Http.HttpRequestException("failed"));

            var rssParsingServiceMock = new Mock<IRssParsingService>();
            var postFilterService = new PostFilterService(config);
            var logger = new NullLogger<FeedManager>();
            var logAggregatorMock = new Mock<FeedCord.Core.Interfaces.ILogAggregator>();

            var feedManager = new FeedManager(config, httpClientMock.Object, rssParsingServiceMock.Object, postFilterService, logger, logAggregatorMock.Object);

            await feedManager.InitializeUrlsAsync();

            await feedManager.CheckForNewPostsAsync();
            await feedManager.CheckForNewPostsAsync();
            await feedManager.CheckForNewPostsAsync();

            var feedData = feedManager.GetAllFeedData();
            Assert.Empty(feedData);
        }
    }
}
