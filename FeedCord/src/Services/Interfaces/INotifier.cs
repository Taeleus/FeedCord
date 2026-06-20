using FeedCord.Common;

namespace FeedCord.Services.Interfaces
{
    public interface INotifier
    {
        Task<bool> SendNotificationAsync(Post post, CancellationToken cancellationToken = default);
    }
}
