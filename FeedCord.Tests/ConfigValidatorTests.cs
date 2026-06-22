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

    [Fact]
    public void ValidateInstances_RejectsEmptyCollection()
    {
        Assert.Throws<ValidationException>(
            () => ConfigValidator.ValidateInstances(Array.Empty<Config>()));
    }

    [Fact]
    public void ValidateInstances_RejectsDuplicateIdsIgnoringCase()
    {
        var first = CreateValidConfig();
        first.Id = "News";
        var second = CreateValidConfig();
        second.Id = "news";

        Assert.Throws<ValidationException>(
            () => ConfigValidator.ValidateInstances(new[] { first, second }));
    }

    [Fact]
    public void LegacyPersistenceProperty_MapsToPersistState()
    {
        var config = CreateValidConfig();

#pragma warning disable CS0618
        config.PersistenceOnShutdown = true;
#pragma warning restore CS0618

        Assert.True(config.PersistState);
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

    [Theory]
    [InlineData(-1)]
    [InlineData(16777216)]
    public void Validate_RejectsColorOutsideDiscordRange(int color)
    {
        var config = CreateValidConfig();
        config.Color = color;

        Assert.Throws<ValidationException>(() => ConfigValidator.Validate(config));
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
            PersistState = true
        };
    }
}
