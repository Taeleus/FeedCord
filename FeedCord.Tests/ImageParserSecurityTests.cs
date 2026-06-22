using FeedCord.Infrastructure.Parsers;
using FeedCord.Infrastructure.Http;
using FeedCord.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace FeedCord.Tests;

public class ImageParserSecurityTests
{
    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://10.0.0.1/image")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://[::1]/image")]
    [InlineData("http://localhost/image")]
    public async Task ServerFetchPolicy_RejectsPrivateNetworkUrls(string url)
    {
        Assert.False(await ServerFetchPolicy.IsPublicHttpUrlAsync(url));
    }

    [Fact]
    public async Task TryExtractImageLink_UsesPublicOnlyHttpPath()
    {
        var httpClient = new Mock<ICustomHttpClient>();
        httpClient.Setup(client => client.GetPublicAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpResponseMessage?)null);
        var parser = new ImageParserService(
            httpClient.Object,
            new NullLogger<ImageParserService>());

        var image = await parser.TryExtractImageLink("https://example.com/post", string.Empty);

        Assert.Equal(string.Empty, image);
        httpClient.Verify(
            client => client.GetPublicAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        httpClient.Verify(
            client => client.GetAsyncWithFallback(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
