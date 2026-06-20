using System.Collections.Concurrent;
using FeedCord.Common;

namespace FeedCord.Core.Interfaces;

public interface ILogAggregator
{
    Task SendToBatchAsync();
    void SetStartTime(DateTimeOffset startTime);
    void SetEndTime(DateTimeOffset endTime);
    void SetNewPostCount(int newPostCount);
    void AddLatestUrlPost(string url, Post? post);
    void AddUrlResponse(string url, int status);
    void Reset();
    ConcurrentDictionary<string, int> GetUrlResponses();
}
