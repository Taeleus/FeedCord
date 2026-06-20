using FeedCord.Common;
using FeedCord.Services.Interfaces;
using System.Xml.Linq;
using HtmlAgilityPack;
using Microsoft.Extensions.Logging;

namespace FeedCord.Infrastructure.Parsers
{
    public class YoutubeParsingService : IYoutubeParsingService
    {
        private readonly ICustomHttpClient _httpClient;
        private readonly ILogger<YoutubeParsingService> _logger;
        public YoutubeParsingService(ICustomHttpClient httpClient, ILogger<YoutubeParsingService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<Post?> GetXmlUrlAndFeed(
            string xml,
            CancellationToken cancellationToken = default)
        {
            if (IsFeedUrl(xml))
            {
                return await GetRecentPost(xml, cancellationToken);
            }


            var doc = new HtmlDocument();
            doc.LoadHtml(xml);

            var node = doc.DocumentNode.SelectSingleNode("//link[@rel='alternate' and @type='application/rss+xml']");

            if (node != null)
            {
                var hrefValue = node.GetAttributeValue("href", "");
                return await GetRecentPost(hrefValue, cancellationToken);
            }

            _logger.LogWarning("No RSS feed link found in the provided XML.");
            return null;
        }

        private static bool IsFeedUrl(string value)
        {
            return Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
                   (uri.AbsolutePath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) ||
                    uri.AbsolutePath.Equals("/feeds/videos.xml", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<Post?> GetRecentPost(
            string xmlUrl,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(xmlUrl))
            {
                return null;
            }

            try
            {
                using var response = await _httpClient.GetAsyncWithFallback(xmlUrl, cancellationToken);

                if (response is null) return null;

                // Check for success status code before processing
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Failed to fetch YouTube feed from {Url}: HTTP {StatusCode}", xmlUrl, response.StatusCode);
                    return null;
                }

                var xmlContent = await response.Content.ReadAsStringAsync(cancellationToken);

                // Validate content is XML before parsing
                if (string.IsNullOrWhiteSpace(xmlContent) ||
                    !xmlContent.Contains("<feed", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning(
                        "Invalid YouTube feed response from {Url}. Response did not contain an Atom feed.",
                        xmlUrl);

                    return null;
                }

                var xdoc = XDocument.Parse(xmlContent);
                if (xdoc.Root == null) return null;

                XNamespace atomNs = "http://www.w3.org/2005/Atom";
                XNamespace mediaNs = "http://search.yahoo.com/mrss/";

                var channelTitle = xdoc.Root.Element(atomNs + "title")?.Value ?? string.Empty;
                var videoEntry = xdoc.Root.Element(atomNs + "entry");

                if (videoEntry is null)
                {
                    return null;
                }

                var videoTitle = videoEntry.Element(atomNs + "title")?.Value ?? string.Empty;
                var videoId = videoEntry.Element(atomNs + "id")?.Value ?? string.Empty;
                var videoLink = videoEntry.Element(atomNs + "link")?.Attribute("href")?.Value ?? string.Empty;
                var videoThumbnail = videoEntry.Element(mediaNs + "group")?.Element(mediaNs + "thumbnail")?.Attribute("url")?.Value ?? string.Empty;
                var publishedValue = videoEntry.Element(atomNs + "published")?.Value;
                if (!DateTimeOffset.TryParse(publishedValue, out var videoPublished))
                    throw new InvalidDataException("YouTube entry has no valid publication date.");
                var videoAuthor = videoEntry.Element(atomNs + "author")?.Element(atomNs + "name")?.Value ?? string.Empty;


                return new Post(
                    videoTitle,
                    videoThumbnail,
                    string.Empty,
                    videoLink,
                    channelTitle,
                    videoPublished.ToUniversalTime(),
                    videoAuthor,
                    Array.Empty<string>(),
                    videoId);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving YouTube RSS feed from {Url}", xmlUrl);
                throw new InvalidDataException($"Failed to retrieve or parse YouTube feed '{xmlUrl}'.", ex);
            }
        }
    }
}
