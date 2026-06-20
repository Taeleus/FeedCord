using FeedCord.Common;

namespace FeedCord.Services.Interfaces
{
    public interface IFeedManager
    {
        Task<List<PendingPost>> CheckForNewPostsAsync(CancellationToken cancellationToken = default);
        Task InitializeUrlsAsync(CancellationToken cancellationToken = default);
        Task AcknowledgePostsAsync(
            IReadOnlyCollection<PendingPost> pendingPosts,
            CancellationToken cancellationToken = default);
        Task SaveStateAsync(CancellationToken cancellationToken = default);
        IReadOnlyDictionary<string, FeedState> GetAllFeedData();
    }
}
