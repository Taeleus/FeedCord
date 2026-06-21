using System.Xml.Linq;
using FeedCord.Common;
using FeedCord.Services.Interfaces;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace FeedCord.Infrastructure.Parsers;

public class YoutubeParsingService : IYoutubeParsingService
{
    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace MediaNamespace = "http://search.yahoo.com/mrss/";
    private readonly ICustomHttpClient _httpClient;
    private readonly ILogger<YoutubeParsingService> _logger;

    public YoutubeParsingService(
        ICustomHttpClient httpClient,
        ILogger<YoutubeParsingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<List<Post?>> GetXmlUrlAndFeed(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (IsFeedUrl(input))
            return await GetPostsAsync(input, cancellationToken);

        var document = new HtmlDocument();
        document.LoadHtml(input);

        var node = document.DocumentNode.SelectSingleNode(
            "//link[@rel='alternate' and @type='application/rss+xml']");
        if (node is null)
            throw new InvalidDataException("No YouTube RSS feed link was found in the channel page.");

        var feedUrl = node.GetAttributeValue("href", string.Empty);
        return await GetPostsAsync(feedUrl, cancellationToken);
    }

    private async Task<List<Post?>> GetPostsAsync(
        string feedUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(feedUrl))
            throw new InvalidDataException("The YouTube RSS feed URL is empty.");

        try
        {
            using var response = await _httpClient.GetAsyncWithFallback(feedUrl, cancellationToken);
            if (response is null)
                throw new HttpRequestException("YouTube returned no response.");

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"YouTube feed returned HTTP {response.StatusCode}.",
                    null,
                    response.StatusCode);
            }

            var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var document = XDocument.Parse(xmlContent);
            if (document.Root is null ||
                document.Root.Name != AtomNamespace + "feed")
            {
                throw new InvalidDataException("YouTube response was not an Atom feed.");
            }

            var channelTitle = document.Root.Element(AtomNamespace + "title")?.Value
                ?? string.Empty;

            return document.Root
                .Elements(AtomNamespace + "entry")
                .Select(entry => BuildPost(entry, channelTitle))
                .Cast<Post?>()
                .ToList();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving YouTube RSS feed from {Url}", feedUrl);
            throw new InvalidDataException(
                $"Failed to retrieve or parse YouTube feed '{feedUrl}'.",
                ex);
        }
    }

    private static Post BuildPost(XElement entry, string channelTitle)
    {
        var publishedValue = entry.Element(AtomNamespace + "published")?.Value;
        if (!DateTimeOffset.TryParse(publishedValue, out var published))
            throw new InvalidDataException("YouTube entry has no valid publication date.");

        return new Post(
            entry.Element(AtomNamespace + "title")?.Value ?? string.Empty,
            entry.Element(MediaNamespace + "group")
                ?.Element(MediaNamespace + "thumbnail")
                ?.Attribute("url")
                ?.Value ?? string.Empty,
            string.Empty,
            entry.Elements(AtomNamespace + "link")
                .FirstOrDefault(link =>
                    string.Equals(
                        link.Attribute("rel")?.Value,
                        "alternate",
                        StringComparison.OrdinalIgnoreCase))
                ?.Attribute("href")
                ?.Value
                ?? entry.Element(AtomNamespace + "link")?.Attribute("href")?.Value
                ?? string.Empty,
            channelTitle,
            published.ToUniversalTime(),
            entry.Element(AtomNamespace + "author")
                ?.Element(AtomNamespace + "name")
                ?.Value ?? string.Empty,
            Array.Empty<string>(),
            entry.Element(AtomNamespace + "id")?.Value);
    }

    private static bool IsFeedUrl(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
               (uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                uri.AbsolutePath.Equals("/feeds/videos.xml", StringComparison.OrdinalIgnoreCase));
    }
}
