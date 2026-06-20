

namespace FeedCord.Common
{
    public class FeedState
    {
        public bool IsYoutube { get; init; }
        public DateTimeOffset LastPublishDate { get; set; }
        public HashSet<string> ItemIdsAtLastPublishDate { get; set; } = new(StringComparer.Ordinal);
        public int ErrorCount { get; set; }
    }
}
