namespace FeedCord.Common
{
    public class ReferencePost
    {
        public bool IsYoutube { get; set; }
        public DateTimeOffset LastRunDate { get; init; }
        public HashSet<string> ItemIdsAtLastRunDate { get; init; } = new(StringComparer.Ordinal);
    }
}
