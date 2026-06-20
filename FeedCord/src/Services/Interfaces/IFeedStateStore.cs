using FeedCord.Common;

namespace FeedCord.Services.Interfaces;

public interface IFeedStateStore
{
    Task<IReadOnlyDictionary<string, ReferencePost>> LoadAsync(
        string instanceId,
        CancellationToken cancellationToken = default);
    Task SaveAsync(
        string instanceId,
        IReadOnlyDictionary<string, FeedState> states,
        CancellationToken cancellationToken = default);
}
