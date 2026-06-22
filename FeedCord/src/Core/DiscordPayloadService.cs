using FeedCord.Common;
using FeedCord.Core.Interfaces;
using System.Text;
using System.Text.Json;

namespace FeedCord.Core
{
    public class DiscordPayloadService : IDiscordPayloadService
    {
        private readonly Config _config;

        public DiscordPayloadService(Config config)
        {
            _config = config;
        }

        public StringContent BuildPayloadWithPost(Post post)
        {
            if (_config.MarkdownFormat)
                return GenerateMarkdown(post);

            var payload = new
            {
                username = Truncate(_config.Username ?? "FeedCord", 80),
                avatar_url = SanitizeHttpUrl(_config.AvatarUrl),
                allowed_mentions = BuildAllowedMentions(),
                embeds = new[]
                {
                    new
                    {
                        title = SanitizeTitle(post),
                        author = BuildAuthor(_config.AuthorName ?? post.Author),
                        url = SanitizeHttpUrl(post.Link),
                        description = Truncate(post.Description, 4096),
                        image = BuildImage(post.ImageUrl),
                        footer = new
                        {
                            text = BuildFooterText(post),
                            icon_url = SanitizeHttpUrl(_config.FooterImage)
                        },
                        color = _config.Color,
                    }
                }
            };

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return new StringContent(payloadJson, Encoding.UTF8, "application/json");
        }

        public StringContent BuildForumWithPost(Post post)
        {
            if (_config.MarkdownFormat)
                return GenerateMarkdown(post);

            var payload = new
            {
                content = Truncate(post.Tag, 2000),
                allowed_mentions = BuildAllowedMentions(),
                embeds = new[]
                {
                    new
                    {
                        title = SanitizeTitle(post),
                        author = BuildAuthor(_config.AuthorName ?? post.Author),
                        url = SanitizeHttpUrl(post.Link),
                        description = Truncate(post.Description, 4096),
                        image = BuildImage(post.ImageUrl),
                        footer = new
                        {
                            text = BuildFooterText(post),
                            icon_url = SanitizeHttpUrl(_config.FooterImage)
                        },
                        color = _config.Color,
                    }
                },
                thread_name = SanitizeThreadName(post.Title)
            };

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return new StringContent(payloadJson, Encoding.UTF8, "application/json");
        }

        private StringContent GenerateMarkdown(Post post)
        {
            var source = SanitizeHttpUrl(post.Link) is { } safeLink
                ? $"[Source]({safeLink})"
                : string.Empty;
            var markdownPost = $"""
                                # {post.Title}

                                > **Published**: {post.PublishDate:MMMM dd, yyyy}  
                                > **Author**: {post.Author}  
                                > **Feed**: {post.Tag}

                                {post.Description}

                                {source}

                                """;
            object payload;

            if (_config.Forum)
            {
                payload = new
                {
                    content = Truncate(markdownPost, 2000),
                    thread_name = SanitizeThreadName(post.Title),
                    allowed_mentions = BuildAllowedMentions()
                };
            }
            else
            {
                payload = new
                {
                    content = Truncate(markdownPost, 2000),
                    allowed_mentions = BuildAllowedMentions()
                };
            }

            var payloadJson = JsonSerializer.Serialize(payload, new JsonSerializerOptions
            {
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            return new StringContent(payloadJson, Encoding.UTF8, "application/json");
        }

        private object? BuildAuthor(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return new
            {
                name = Truncate(name, 256),
                url = SanitizeHttpUrl(_config.AuthorUrl),
                icon_url = SanitizeHttpUrl(_config.AuthorIcon)
            };
        }

        private object? BuildImage(string? postImageUrl)
        {
            var imageUrl = string.IsNullOrWhiteSpace(postImageUrl) ? _config.FallbackImage : postImageUrl;
            var safeImageUrl = SanitizeHttpUrl(imageUrl);
            return safeImageUrl is null ? null : new { url = safeImageUrl };
        }

        private static object BuildAllowedMentions()
        {
            return new { parse = Array.Empty<string>() };
        }

        private static string BuildFooterText(Post post)
        {
            return Truncate($"{post.Tag} - {post.PublishDate:MM/dd/yyyy h:mm tt}", 1024);
        }

        private static string SanitizeTitle(Post post)
        {
            var title = RemoveControlCharacters(post.Title).Trim();
            if (string.IsNullOrWhiteSpace(title))
                title = "FeedCord post";

            return Truncate(title, 256);
        }

        private static string SanitizeThreadName(string? value)
        {
            var threadName = RemoveControlCharacters(value).Trim();
            if (string.IsNullOrWhiteSpace(threadName))
                threadName = "FeedCord post";

            return Truncate(threadName, 100);
        }

        private static string RemoveControlCharacters(string? value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return new string(value.Where(character => !char.IsControl(character)).ToArray());
        }

        private static string? SanitizeHttpUrl(string? value)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            return uri.AbsoluteUri;
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
