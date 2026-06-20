namespace FeedCord.Common;

public sealed record PendingPost(string FeedUrl, Post Post, bool ShouldNotify = true);
