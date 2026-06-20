using FeedCord.Common;
using FeedCord.Core.Interfaces;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace FeedCord.Infrastructure.Workers
{
    public class FeedWorker : BackgroundService
    {
        private readonly IHostApplicationLifetime _lifetime;
        private readonly ILogAggregator _logAggregator;
        private readonly ILogger<FeedWorker> _logger;
        private readonly IFeedManager _feedManager;
        private readonly INotifier _notifier;

        private readonly bool _persistent;
        private readonly string _id;
        private readonly int _delayTime;
        private bool _isInitialized;


        public FeedWorker(
            IHostApplicationLifetime lifetime,
            ILogger<FeedWorker> logger,
            IFeedManager feedManager,
            INotifier notifier,
            Config config,
            ILogAggregator logAggregator)
        {
            _lifetime = lifetime;
            _logger = logger;
            _feedManager = feedManager;
            _notifier = notifier;
            _delayTime = config.RssCheckIntervalMinutes;
            _id = config.Id;
            _isInitialized = false;
            _persistent = config.PersistenceOnShutdown;
            _logAggregator = logAggregator;

            logger.LogInformation("{id} Created with check interval {Interval} minutes",
                _id, config.RssCheckIntervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _lifetime.ApplicationStopping.Register(OnShutdown);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logAggregator.SetStartTime(DateTimeOffset.UtcNow);

                try
                {
                    await RunRoutineBackgroundProcessAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Error in Background Process loop. Continuing to next interval.");
                }

                _logAggregator.SetEndTime(DateTimeOffset.UtcNow);

                try
                {
                    await _logAggregator.SendToBatchAsync();
                }
                catch (Exception e)
                {
                    _logger.LogError(e, "Failed to send batch logs. Continuing to next interval.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(_delayTime), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    // Shutdown requested, exit cleanly
                    break;
                }
            }
        }

        private async Task RunRoutineBackgroundProcessAsync(CancellationToken cancellationToken)
        {
            if (!_isInitialized)
            {
                _logger.LogInformation("{id}: Initializing Url Checks..", _id);
                await _feedManager.InitializeUrlsAsync(cancellationToken);
                _isInitialized = true;
            }

            var posts = await _feedManager.CheckForNewPostsAsync(cancellationToken);

            if (posts.Count > 0)
            {
                var notificationCount = posts.Count(post => post.ShouldNotify);
                _logger.LogInformation("{id}: Found {PostCount} new posts", _id, notificationCount);

                foreach (var feedGroup in posts.GroupBy(post => post.FeedUrl))
                {
                    var orderedPosts = feedGroup.OrderBy(post => post.Post.PublishDate).ToList();
                    var deliverySucceeded = true;

                    foreach (var pendingPost in orderedPosts)
                    {
                        if (pendingPost.ShouldNotify &&
                            !await _notifier.SendNotificationAsync(pendingPost.Post, cancellationToken))
                        {
                            _logger.LogWarning(
                                "{id}: Delivery failed for {PostUrl}; later posts from this feed will be retried",
                                _id,
                                pendingPost.Post.Link);
                            deliverySucceeded = false;
                            break;
                        }
                    }

                    // Checkpoint the feed only after its complete batch succeeds. This avoids
                    // losing posts that share a publication timestamp or follow a failed post.
                    if (deliverySucceeded)
                        await _feedManager.AcknowledgePostsAsync(orderedPosts, cancellationToken);
                }
            }
        }

        private void OnShutdown()
        {
            if (!_persistent) return;

            try
            {
                _feedManager.SaveStateAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{id}: Failed to persist feed state during shutdown", _id);
            }
        }
    }
}
