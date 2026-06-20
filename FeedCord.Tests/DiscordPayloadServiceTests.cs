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
            PersistenceOnShutdown = false
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
    }
}
