using FeedCord.Infrastructure.Parsers;
using FeedCord.Services;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FeedCord.Tests;

public class ParserFixtureTests
{
    [Theory]
    [InlineData("rss.xml", "rss-item-1", "RSS fixture item")]
    [InlineData("atom.xml", "atom-item-1", "Atom fixture item")]
    public async Task RssParser_ParsesStableIdentityAndUtcDate(
        string fixtureName,
        string expectedId,
        string expectedTitle)
    {
        var imageParser = new Mock<IImageParserService>();
        imageParser.Setup(service => service.TryExtractImageLink(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);
        var service = new RssParsingService(
            new NullLogger<RssParsingService>(),
            Mock.Of<IYoutubeParsingService>(),
            imageParser.Object);

        var posts = await service.ParseRssFeedAsync(await ReadFixtureAsync(fixtureName), 500);
        var post = Assert.Single(posts)!;

        Assert.Equal(expectedId, post.ItemId);
        Assert.Equal(expectedTitle, post.Title);
        Assert.Equal(TimeSpan.Zero, post.PublishDate.Offset);
    }

    [Fact]
    public async Task YoutubeParser_ParsesFixtureIdentityAndUtcDate()
    {
        var client = new Mock<ICustomHttpClient>();
        client.Setup(service => service.GetAsyncWithFallback(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(await ReadFixtureAsync("youtube.xml"))
            });
        var service = new YoutubeParsingService(
            client.Object,
            new NullLogger<YoutubeParsingService>());

        var posts = await service.GetXmlUrlAndFeed(
            "https://www.youtube.com/feeds/videos.xml?channel_id=fixture");
        var post = posts[0];

        Assert.Equal(3, posts.Count);
        Assert.NotNull(post);
        Assert.Equal("yt:video:fixture16", post.ItemId);
        Assert.Equal("Fixture Video 16", post.Title);
        Assert.Equal(TimeSpan.Zero, post.PublishDate.Offset);
    }

    private static Task<string> ReadFixtureAsync(string name)
    {
        return File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
    }
}
