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
                username = _config.Username ?? "FeedCord",
                avatar_url = _config.AvatarUrl ?? "",
                embeds = new[]
                {
                    new
                    {
                        title = Truncate(post.Title, 256),
                        author = BuildAuthor(_config.AuthorName ?? post.Author),
                        url = post.Link,
                        description = Truncate(post.Description, 4096),
                        image = BuildImage(post.ImageUrl),
                        footer = new
                        {
                            text = $"{post.Tag} - {post.PublishDate:MM/dd/yyyy h:mm tt}",
                            icon_url = _config.FooterImage ?? ""
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
                embeds = new[]
                {
                    new
                    {
                        title = Truncate(post.Title, 256),
                        author = BuildAuthor(_config.AuthorName ?? post.Author),
                        url = post.Link,
                        description = Truncate(post.Description, 4096),
                        image = BuildImage(post.ImageUrl),
                        footer = new
                        {
                            text = $"{post.Tag} - {post.PublishDate:MM/dd/yyyy h:mm tt}",
                            icon_url = _config.FooterImage ?? ""
                        },
                        color = _config.Color,
                    }
                },
                thread_name = Truncate(post.Title, 100)
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
            var markdownPost = $"""
                                # {post.Title}

                                > **Published**: {post.PublishDate:MMMM dd, yyyy}  
                                > **Author**: {post.Author}  
                                > **Feed**: {post.Tag}

                                {post.Description}

                                [Source]({post.Link})

                                """;
            object payload;

            if (_config.Forum)
            {
                payload = new
                {
                    content = Truncate(markdownPost, 2000),
                    thread_name = Truncate(post.Title, 100)
                };
            }
            else
            {
                payload = new
                {
                    content = Truncate(markdownPost, 2000)
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
                url = _config.AuthorUrl,
                icon_url = _config.AuthorIcon
            };
        }

        private object? BuildImage(string? postImageUrl)
        {
            var imageUrl = string.IsNullOrWhiteSpace(postImageUrl) ? _config.FallbackImage : postImageUrl;
            return string.IsNullOrWhiteSpace(imageUrl) ? null : new { url = imageUrl };
        }

        private static string Truncate(string? value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Length <= maxLength ? value : value[..maxLength];
        }
    }
}
