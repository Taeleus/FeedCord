using Microsoft.Extensions.Logging;
using CodeHollow.FeedReader;
using FeedCord.Common;
using FeedCord.Services.Helpers;
using FeedCord.Services.Interfaces;

namespace FeedCord.Services
{
    public class RssParsingService : IRssParsingService
    {
        private readonly ILogger<RssParsingService> _logger;
        private readonly IYoutubeParsingService _youtubeParsingService;
        private readonly IImageParserService _imageParserService;

        public RssParsingService(
            ILogger<RssParsingService> logger,
            IYoutubeParsingService youtubeParsingService,
            IImageParserService imageParserService)
        {
            _logger = logger;
            _youtubeParsingService = youtubeParsingService;
            _imageParserService = imageParserService;
        }

        public async Task<List<Post?>> ParseRssFeedAsync(
            string xmlContent,
            int trim,
            CancellationToken cancellationToken = default)
        {
            var xmlContenter = xmlContent.Replace("<!doctype", "<!DOCTYPE");

            try
            {
                var feed = FeedReader.ReadFromString(xmlContenter);

                var latestPost = feed.Items.FirstOrDefault();

                if (latestPost is null)
                    return new List<Post?>();

                var feedItems = feed.Items.ToList();

                List<Post?> posts = new();

                foreach (var post in feedItems)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var rawXml = GetRawXmlForItem(post);

                    var imageLink = await _imageParserService
                        .TryExtractImageLink(post.Link, rawXml, cancellationToken)
                                    ?? feed.ImageUrl;

                    var builtPost = PostBuilder.TryBuildPost(post, feed, trim, imageLink);

                    posts.Add(builtPost);
                }

                return posts;

            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse RSS feed content");
                throw new InvalidDataException("Failed to parse RSS feed content.", ex);
            }
        }

        public async Task<List<Post?>> ParseYoutubeFeedAsync(
            string youtubeInput,
            CancellationToken cancellationToken = default)
        {
            var youtubePosts = await _youtubeParsingService.GetXmlUrlAndFeed(
                youtubeInput,
                cancellationToken);

            if (youtubePosts.Count == 0)
            {
                _logger.LogInformation(
                    "YouTube feed contained no video entries. Input preview: {YoutubeInput}",
                    SafeForLog(youtubeInput));
            }

            return youtubePosts;
        }

        private static string SafeForLog(string? value, int maxLength = 300)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<empty>";

            value = value.Replace("\r", " ").Replace("\n", " ").Trim();

            if (value.Length <= maxLength)
                return value;

            return value[..maxLength] + "... [truncated]";
        }

        private string GetRawXmlForItem(FeedItem feedItem)
        {
            if (feedItem.SpecificItem is CodeHollow.FeedReader.Feeds.Rss20FeedItem rssItem)
            {
                return rssItem.Element?.ToString() ?? "";
            }
            else if (feedItem.SpecificItem is CodeHollow.FeedReader.Feeds.AtomFeedItem atomItem)
            {
                return atomItem.Element?.ToString() ?? "";
            }

            return "";
        }

    }
}
