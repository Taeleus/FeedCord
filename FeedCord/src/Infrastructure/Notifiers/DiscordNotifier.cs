using FeedCord.Common;
using FeedCord.Core.Interfaces;
using FeedCord.Services.Interfaces;

namespace FeedCord.Infrastructure.Notifiers
{
    internal class DiscordNotifier : INotifier
    {
        private readonly ICustomHttpClient _httpClient;
        private readonly IDiscordPayloadService _discordPayloadService;
        private readonly string _webhook;
        private readonly bool _forum;
        public DiscordNotifier(Config config, ICustomHttpClient httpClient, IDiscordPayloadService discordPayloadService)
        {
            _httpClient = httpClient;
            _discordPayloadService = discordPayloadService;
            _webhook = config.DiscordWebhookUrl;
            _forum = config.Forum;
        }
        public async Task<bool> SendNotificationAsync(Post post, CancellationToken cancellationToken = default)
        {
            using var forumChannelContent = _discordPayloadService.BuildForumWithPost(post);
            using var textChannelContent = _discordPayloadService.BuildPayloadWithPost(post);

            return await _httpClient.PostAsyncWithFallback(
                _webhook,
                forumChannelContent,
                textChannelContent,
                _forum,
                cancellationToken);
        }
    }
}
