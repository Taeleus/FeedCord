using FeedCord.Common;
using FeedCord.Core.Interfaces;
using FeedCord.Infrastructure.Workers;
using FeedCord.Services;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FeedCord.Tests;

public class DeliverySemanticsTests
{
    [Fact]
    public async Task FeedManager_DoesNotAdvanceCheckpoint_UntilPostIsAcknowledged()
    {
        var config = CreateConfig(persistence: true);
        var httpClient = new Mock<ICustomHttpClient>();
        httpClient.Setup(client => client.GetAsyncWithFallback(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<rss></rss>")
            });

        var post = CreatePost(DateTimeOffset.UtcNow.AddMinutes(1));
        var parser = new Mock<IRssParsingService>();
        parser.Setup(service => service.ParseRssFeedAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Post?> { post });

        var stateStore = new Mock<IFeedStateStore>();
        stateStore.Setup(store => store.LoadAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ReferencePost>());

        var manager = new FeedManager(
            config,
            httpClient.Object,
            parser.Object,
            new PostFilterService(config),
            stateStore.Object,
            new NullLogger<FeedManager>(),
            Mock.Of<ILogAggregator>());

        await manager.InitializeUrlsAsync();
        var originalCheckpoint = manager.GetAllFeedData()[config.RssUrls[0]].LastPublishDate;

        var pending = Assert.Single(await manager.CheckForNewPostsAsync());
        Assert.Equal(originalCheckpoint, manager.GetAllFeedData()[config.RssUrls[0]].LastPublishDate);

        await manager.AcknowledgePostsAsync(new[] { pending });

        Assert.Equal(post.PublishDate, manager.GetAllFeedData()[config.RssUrls[0]].LastPublishDate);
        stateStore.Verify(
            store => store.SaveAsync(
                config.Id,
                It.IsAny<IReadOnlyDictionary<string, FeedState>>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeedManager_ReturnsUnseenItem_WithSamePublicationTimestamp()
    {
        var config = CreateConfig();
        var timestamp = DateTimeOffset.UtcNow.AddMinutes(-1);
        var httpClient = new Mock<ICustomHttpClient>();
        httpClient.Setup(client => client.GetAsyncWithFallback(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<rss></rss>")
            });

        var oldPost = CreatePost(timestamp) with { ItemId = "old-item" };
        var newPost = CreatePost(timestamp) with
        {
            ItemId = "new-item",
            Link = "https://example.com/new"
        };
        var parser = new Mock<IRssParsingService>();
        parser.Setup(service => service.ParseRssFeedAsync(
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Post?> { oldPost, newPost });

        var stateStore = new Mock<IFeedStateStore>();
        stateStore.Setup(store => store.LoadAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ReferencePost>
            {
                [config.RssUrls[0]] = new()
                {
                    LastRunDate = timestamp,
                    ItemIdsAtLastRunDate = new HashSet<string> { "old-item" }
                }
            });

        var manager = new FeedManager(
            config,
            httpClient.Object,
            parser.Object,
            new PostFilterService(config),
            stateStore.Object,
            new NullLogger<FeedManager>(),
            Mock.Of<ILogAggregator>());

        await manager.InitializeUrlsAsync();
        var pending = Assert.Single(await manager.CheckForNewPostsAsync());

        Assert.Equal("new-item", pending.Post.ItemId);
    }

    [Fact]
    public async Task FeedManager_PropagatesCallerCancellation()
    {
        var config = CreateConfig();
        var httpClient = new Mock<ICustomHttpClient>();
        httpClient.Setup(client => client.GetAsyncWithFallback(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        var manager = new FeedManager(
            config,
            httpClient.Object,
            Mock.Of<IRssParsingService>(),
            new PostFilterService(config),
            new NullLogger<FeedManager>(),
            Mock.Of<ILogAggregator>());
        await manager.InitializeUrlsAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => manager.CheckForNewPostsAsync(cancellation.Token));
    }

    [Fact]
    public async Task FeedManager_ReturnsEveryMissedYoutubeVideo_AfterPersistedCheckpoint()
    {
        var config = CreateConfig();
        config.RssUrls = Array.Empty<string>();
        config.YoutubeUrls =
        [
            "https://www.youtube.com/feeds/videos.xml?channel_id=fixture"
        ];

        var httpClient = new Mock<ICustomHttpClient>();
        httpClient.Setup(client => client.GetAsyncWithFallback(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent("<feed></feed>")
            });

        var missedPosts = new List<Post?>
        {
            CreatePost(new DateTimeOffset(2025, 6, 16, 12, 0, 0, TimeSpan.Zero)) with
            {
                ItemId = "video-16",
                Title = "Video 16"
            },
            CreatePost(new DateTimeOffset(2025, 6, 15, 12, 0, 0, TimeSpan.Zero)) with
            {
                ItemId = "video-15",
                Title = "Video 15"
            },
            CreatePost(new DateTimeOffset(2025, 6, 14, 12, 0, 0, TimeSpan.Zero)) with
            {
                ItemId = "video-14",
                Title = "Video 14"
            }
        };
        var parser = new Mock<IRssParsingService>();
        parser.Setup(service => service.ParseYoutubeFeedAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(missedPosts);

        var stateStore = new Mock<IFeedStateStore>();
        stateStore.Setup(store => store.LoadAsync(config.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ReferencePost>
            {
                [config.YoutubeUrls[0]] = new()
                {
                    IsYoutube = true,
                    LastRunDate = new DateTimeOffset(2025, 6, 13, 12, 0, 0, TimeSpan.Zero)
                }
            });

        var manager = new FeedManager(
            config,
            httpClient.Object,
            parser.Object,
            new PostFilterService(config),
            stateStore.Object,
            new NullLogger<FeedManager>(),
            Mock.Of<ILogAggregator>());

        await manager.InitializeUrlsAsync();
        var pending = await manager.CheckForNewPostsAsync();

        Assert.Equal(
            ["Video 14", "Video 15", "Video 16"],
            pending.Select(item => item.Post.Title).ToArray());
    }

    [Fact]
    public async Task FeedWorker_DoesNotAcknowledgePost_WhenDeliveryFails()
    {
        var pendingPost = new PendingPost("https://example.com/rss", CreatePost(DateTimeOffset.UtcNow));
        var manager = new Mock<IFeedManager>();
        manager.Setup(service => service.CheckForNewPostsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPost> { pendingPost });

        var attempted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifier = new Mock<INotifier>();
        notifier.Setup(service => service.SendNotificationAsync(pendingPost.Post, It.IsAny<CancellationToken>()))
            .Callback(() => attempted.SetResult())
            .ReturnsAsync(false);

        var worker = CreateWorker(manager.Object, notifier.Object);
        await worker.StartAsync(CancellationToken.None);
        await attempted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        manager.Verify(
            service => service.AcknowledgePostsAsync(
                It.IsAny<IReadOnlyCollection<PendingPost>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task FeedWorker_AcknowledgesPost_WhenDeliverySucceeds()
    {
        var pendingPost = new PendingPost("https://example.com/rss", CreatePost(DateTimeOffset.UtcNow));
        var acknowledged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var manager = new Mock<IFeedManager>();
        manager.Setup(service => service.CheckForNewPostsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPost> { pendingPost });
        manager.Setup(service => service.AcknowledgePostsAsync(
                It.Is<IReadOnlyCollection<PendingPost>>(posts => posts.Contains(pendingPost)),
                It.IsAny<CancellationToken>()))
            .Callback(() => acknowledged.SetResult())
            .Returns(Task.CompletedTask);

        var notifier = new Mock<INotifier>();
        notifier.Setup(service => service.SendNotificationAsync(pendingPost.Post, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var worker = CreateWorker(manager.Object, notifier.Object);
        await worker.StartAsync(CancellationToken.None);
        await acknowledged.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        manager.Verify(
            service => service.AcknowledgePostsAsync(
                It.Is<IReadOnlyCollection<PendingPost>>(posts => posts.Contains(pendingPost)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task FeedWorker_DoesNotCheckpointPartialBatch_WhenLaterDeliveryFails()
    {
        var publishDate = DateTimeOffset.UtcNow;
        var first = new PendingPost("https://example.com/rss", CreatePost(publishDate));
        var second = new PendingPost(
            first.FeedUrl,
            CreatePost(publishDate) with { Link = "https://example.com/second" });
        var manager = new Mock<IFeedManager>();
        manager.Setup(service => service.CheckForNewPostsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PendingPost> { first, second });

        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var notifier = new Mock<INotifier>();
        notifier.SetupSequence(service =>
                service.SendNotificationAsync(It.IsAny<Post>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);
        notifier.Setup(service => service.SendNotificationAsync(second.Post, It.IsAny<CancellationToken>()))
            .Callback(() => failed.SetResult())
            .ReturnsAsync(false);

        var worker = CreateWorker(manager.Object, notifier.Object);
        await worker.StartAsync(CancellationToken.None);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await worker.StopAsync(CancellationToken.None);

        manager.Verify(
            service => service.AcknowledgePostsAsync(
                It.IsAny<IReadOnlyCollection<PendingPost>>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static FeedWorker CreateWorker(IFeedManager manager, INotifier notifier)
    {
        var lifetime = new Mock<IHostApplicationLifetime>();
        lifetime.SetupGet(value => value.ApplicationStarted).Returns(CancellationToken.None);
        lifetime.SetupGet(value => value.ApplicationStopping).Returns(CancellationToken.None);
        lifetime.SetupGet(value => value.ApplicationStopped).Returns(CancellationToken.None);

        return new FeedWorker(
            lifetime.Object,
            new NullLogger<FeedWorker>(),
            manager,
            notifier,
            CreateConfig(),
            Mock.Of<ILogAggregator>());
    }

    private static Config CreateConfig(bool persistence = false)
    {
        return new Config
        {
            Id = "test",
            RssUrls = new[] { "https://example.com/rss" },
            YoutubeUrls = Array.Empty<string>(),
            DiscordWebhookUrl = "https://discord.com/api/webhooks/1/token",
            RssCheckIntervalMinutes = 1,
            DescriptionLimit = 500,
            Forum = false,
            MarkdownFormat = false,
            PersistenceOnShutdown = persistence
        };
    }

    private static Post CreatePost(DateTimeOffset publishDate)
    {
        return new Post(
            "Title",
            "",
            "Description",
            "https://example.com/post",
            "Example",
            publishDate,
            "Author");
    }
}
