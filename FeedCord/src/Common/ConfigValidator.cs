using System.ComponentModel.DataAnnotations;

namespace FeedCord.Common;

public static class ConfigValidator
{
    public static void ValidateInstances(IReadOnlyCollection<Config> configs)
    {
        ArgumentNullException.ThrowIfNull(configs);

        if (configs.Count == 0)
            throw new ValidationException("At least one feed instance must be configured.");

        var duplicateIds = configs
            .Where(config => !string.IsNullOrWhiteSpace(config.Id))
            .GroupBy(config => config.Id.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateIds.Length > 0)
        {
            throw new ValidationException(
                $"Instance IDs must be unique. Duplicates: {string.Join(", ", duplicateIds)}");
        }

        foreach (var config in configs)
            Validate(config);
    }

    public static void Validate(Config config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var context = new ValidationContext(config);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(config, context, results, validateAllProperties: true);

        if (!Uri.TryCreate(config.DiscordWebhookUrl, UriKind.Absolute, out var webhookUri) ||
            webhookUri.Scheme != Uri.UriSchemeHttps ||
            !IsDiscordHost(webhookUri.Host) ||
            !webhookUri.AbsolutePath.StartsWith("/api/webhooks/", StringComparison.OrdinalIgnoreCase))
        {
            results.Add(new ValidationResult("DiscordWebhookUrl must be a valid HTTPS Discord webhook URL."));
        }

        var feedUrls = (config.RssUrls ?? []).Concat(config.YoutubeUrls ?? []).ToArray();
        if (feedUrls.All(string.IsNullOrWhiteSpace))
            results.Add(new ValidationResult("At least one RSS or YouTube URL must be configured."));

        if (feedUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Any(url => !Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
                        (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
        {
            results.Add(new ValidationResult("All feed URLs must be absolute HTTP or HTTPS URLs."));
        }

        if (config.PostFilters?.Any(filter =>
                string.IsNullOrWhiteSpace(filter.Url) ||
                filter.Filters is null ||
                filter.Filters.Any(string.IsNullOrWhiteSpace)) == true)
        {
            results.Add(new ValidationResult("PostFilters must contain a URL and non-empty filter values."));
        }

        if (results.Count == 0)
            return;

        var errors = string.Join(Environment.NewLine, results.Select(result => result.ErrorMessage));
        throw new ValidationException($"Invalid config entry:{Environment.NewLine}{errors}");
    }

    public static int ValidateGlobalConcurrency(int concurrentRequests)
    {
        if (concurrentRequests is < 1 or > 100)
            throw new ValidationException("Global ConcurrentRequests must be between 1 and 100.");

        return concurrentRequests;
    }

    private static bool IsDiscordHost(string host)
    {
        return host.Equals("discord.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".discord.com", StringComparison.OrdinalIgnoreCase) ||
               host.Equals("discordapp.com", StringComparison.OrdinalIgnoreCase) ||
               host.EndsWith(".discordapp.com", StringComparison.OrdinalIgnoreCase);
    }
}
