using FeedCord.Infrastructure.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace FeedCord.Tests;

public class DiscordIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ConfiguredWebhook_AcceptsTestPayload()
    {
        var webhook = Environment.GetEnvironmentVariable("FEEDCORD_TEST_DISCORD_WEBHOOK");
        if (string.IsNullOrWhiteSpace(webhook))
            return;

        var client = new CustomHttpClient(
            new NullLogger<CustomHttpClient>(),
            new HttpClient { Timeout = TimeSpan.FromSeconds(15) },
            new SemaphoreSlim(1));
        using var forumPayload = new StringContent(
            """{"content":"FeedCord integration test","thread_name":"FeedCord integration test"}""",
            System.Text.Encoding.UTF8,
            "application/json");
        using var textPayload = new StringContent(
            """{"content":"FeedCord integration test"}""",
            System.Text.Encoding.UTF8,
            "application/json");

        var accepted = await client.PostAsyncWithFallback(
            webhook,
            forumPayload,
            textPayload,
            isForum: false);

        Assert.True(accepted);
    }
}
