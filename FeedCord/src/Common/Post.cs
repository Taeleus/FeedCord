namespace FeedCord.Common
{
    public record Post(
        string Title,
        string ImageUrl,
        string Description,
        string Link,
        string Tag,
        DateTimeOffset PublishDate,
        string Author,
        string[]? Labels = null,
        string? ItemId = null
        );
}
