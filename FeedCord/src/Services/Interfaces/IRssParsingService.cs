using FeedCord.Common;

namespace FeedCord.Services.Interfaces
{
    public interface IRssParsingService
    {
        Task<List<Post?>> ParseRssFeedAsync(
            string xmlContent,
            int trim,
            CancellationToken cancellationToken = default);
        Task<Post?> ParseYoutubeFeedAsync(
            string channelUrl,
            CancellationToken cancellationToken = default);
    }
}
