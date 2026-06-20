using System.ComponentModel.DataAnnotations;
using FeedCord.Common;

namespace FeedCord.Tests;

public class ConfigValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidConfiguration()
    {
        ConfigValidator.Validate(CreateValidConfig());
    }

    [Theory]
    [InlineData("http://discord.com/api/webhooks/1/token")]
    [InlineData("https://example.com/api/webhooks/1/token")]
    [InlineData("https://discord.com/not-a-webhook")]
    public void Validate_RejectsInvalidWebhook(string webhook)
    {
        var config = CreateValidConfig();
        config.DiscordWebhookUrl = webhook;

        Assert.Throws<ValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void Validate_RejectsMissingFeeds()
    {
        var config = CreateValidConfig();
        config.RssUrls = Array.Empty<string>();

        Assert.Throws<ValidationException>(() => ConfigValidator.Validate(config));
    }

    [Fact]
    public void ValidateGlobalConcurrency_RejectsOutOfRangeValue()
    {
        Assert.Throws<ValidationException>(() => ConfigValidator.ValidateGlobalConcurrency(0));
        Assert.Throws<ValidationException>(() => ConfigValidator.ValidateGlobalConcurrency(101));
    }

    private static Config CreateValidConfig()
    {
        return new Config
        {
            Id = "test",
            RssUrls = new[] { "https://example.com/rss" },
            YoutubeUrls = Array.Empty<string>(),
            DiscordWebhookUrl = "https://discord.com/api/webhooks/1/token",
            RssCheckIntervalMinutes = 5,
            DescriptionLimit = 500,
            Forum = false,
            MarkdownFormat = false,
            PersistenceOnShutdown = true
        };
    }
}
