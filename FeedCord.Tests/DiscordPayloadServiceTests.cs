using System.Text.Json;
using FeedCord.Common;
using FeedCord.Core;

namespace FeedCord.Tests;

public class DiscordPayloadServiceTests
{
    [Fact]
    public async Task BuildPayloadWithPost_ClampsDiscordFieldLengths()
    {
        var config = new Config
        {
            Id = "test",
            RssUrls = Array.Empty<string>(),
            YoutubeUrls = Array.Empty<string>(),
            DiscordWebhookUrl = "https://discord.com/api/webhooks/1/token",
            RssCheckIntervalMinutes = 1,
            DescriptionLimit = 4096,
            Forum = false,
            MarkdownFormat = false,
            PersistState = false
        };
        var post = new Post(
            new string('t', 300),
            "",
            new string('d', 5000),
            "https://example.com/post",
            "tag",
            DateTimeOffset.UtcNow,
            "");

        using var payload = new DiscordPayloadService(config).BuildPayloadWithPost(post);
        using var document = JsonDocument.Parse(await payload.ReadAsStringAsync());
        var embed = document.RootElement.GetProperty("embeds")[0];

        Assert.Equal(256, embed.GetProperty("title").GetString()!.Length);
        Assert.Equal(4096, embed.GetProperty("description").GetString()!.Length);
        Assert.False(embed.TryGetProperty("author", out _));
        Assert.False(embed.TryGetProperty("image", out _));
        Assert.Empty(document.RootElement
            .GetProperty("allowed_mentions")
            .GetProperty("parse")
            .EnumerateArray());
    }

    [Fact]
    public async Task BuildForumWithPost_SanitizesPoisonFields()
    {
        var config = new Config
        {
            Id = "test",
            RssUrls = new[] { "https://example.com/rss" },
            YoutubeUrls = Array.Empty<string>(),
            DiscordWebhookUrl = "https://discord.com/api/webhooks/1/token",
            RssCheckIntervalMinutes = 1,
            DescriptionLimit = 4096,
            Forum = true,
            MarkdownFormat = false,
            PersistState = false,
            AvatarUrl = "javascript:alert(1)",
            FallbackImage = "file:///etc/passwd"
        };
        var post = new Post(
            "\r\n",
            "data:image/png;base64,abc",
            "@everyone",
            "javascript:alert(1)",
            "@here",
            DateTimeOffset.UtcNow,
            "");

        using var payload = new DiscordPayloadService(config).BuildForumWithPost(post);
        using var document = JsonDocument.Parse(await payload.ReadAsStringAsync());
        var root = document.RootElement;
        var embed = root.GetProperty("embeds")[0];

        Assert.Equal("FeedCord post", root.GetProperty("thread_name").GetString());
        Assert.Equal("FeedCord post", embed.GetProperty("title").GetString());
        Assert.False(embed.TryGetProperty("url", out _));
        Assert.False(embed.TryGetProperty("image", out _));
        Assert.Empty(root.GetProperty("allowed_mentions").GetProperty("parse").EnumerateArray());
    }
}
