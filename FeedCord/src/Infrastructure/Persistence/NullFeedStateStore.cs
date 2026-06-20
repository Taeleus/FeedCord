using FeedCord.Common;
using FeedCord.Services.Interfaces;

namespace FeedCord.Infrastructure.Persistence;

internal sealed class NullFeedStateStore : IFeedStateStore
{
    public static NullFeedStateStore Instance { get; } = new();

    private NullFeedStateStore()
    {
    }

    public Task<IReadOnlyDictionary<string, ReferencePost>> LoadAsync(
        string instanceId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyDictionary<string, ReferencePost> result = new Dictionary<string, ReferencePost>();
        return Task.FromResult(result);
    }

    public Task SaveAsync(
        string instanceId,
        IReadOnlyDictionary<string, FeedState> states,
        CancellationToken cancellationToken = default)
    {
        return Task.CompletedTask;
    }
}
