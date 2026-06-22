![FeedCord banner](FeedCord/docs/images/FeedCord.png)

# FeedCord

FeedCord is a self-hosted RSS, Atom, and YouTube feed reader that publishes new items to Discord text or forum channels through webhooks.

This repository is the Taeleus fork of [Qolors/FeedCord](https://github.com/Qolors/FeedCord). It focuses on reliable delivery, durable checkpoints, strict configuration validation, and predictable operation.

## Features

- RSS 2.0, Atom, Reddit, GitLab, and YouTube feed support
- Discord text-channel and forum-channel webhooks
- Multiple independent feed instances in one process
- Embed or Markdown output
- Per-feed and global post filters
- Image extraction from feed metadata and linked pages
- Global and per-instance request limits
- Discord rate-limit handling
- Durable, per-instance delivery checkpoints
- Automatic migration from the legacy `feed_dump.csv` format
- Multi-architecture container images for `linux/amd64` and `linux/arm64`
- Non-root container execution with a built-in heartbeat health check

![FeedCord gallery example](FeedCord/docs/images/gallery1.png)

## Delivery behavior

FeedCord checkpoints each post after Discord accepts it. Filtered posts are checkpointed without being sent.

This provides at-least-once delivery:

- Failed Discord deliveries are retried during the next feed check.
- Posts are not silently discarded when Discord is unavailable.
- If a later post fails, earlier successful posts remain checkpointed and are not resent.
- A crash after Discord accepts a post but before its checkpoint is saved can still produce a duplicate.

Checkpoints use UTC publication times and stable feed-item IDs. Separate posts that share the same publication timestamp are therefore processed independently.

## Requirements

Choose one:

- Docker or another OCI-compatible container runtime
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) for source builds

You also need:

- A Discord webhook
- At least one RSS, Atom, or YouTube feed
- A writable state directory when persistence is enabled

## Configuration

Create an `appsettings.json` file:

```json
{
  "Instances": [
    {
      "Id": "Engineering News",
      "RssUrls": [
        "https://github.com/Taeleus/FeedCord/releases.atom"
      ],
      "YoutubeUrls": [],
      "DiscordWebhookUrl": "https://discord.com/api/webhooks/WEBHOOK_ID/WEBHOOK_TOKEN",
      "RssCheckIntervalMinutes": 15,
      "DescriptionLimit": 500,
      "Color": 8411391,
      "Forum": false,
      "MarkdownFormat": false,
      "PersistState": true,
      "EnableAutoRemove": false,
      "ConcurrentRequests": 5
    }
  ],
  "ConcurrentRequests": 20
}
```

Important constraints:

- At least one instance must be configured.
- Instance IDs must be unique, ignoring letter case.
- `Id` must identify the instance.
- `RssUrls` and `YoutubeUrls` must both exist; use an empty array when unused.
- At least one feed URL must be configured.
- Feed URLs must use HTTP or HTTPS.
- `DiscordWebhookUrl` must be an HTTPS Discord webhook URL.
- `RssCheckIntervalMinutes` must be between 1 and 1440.
- `DescriptionLimit` must be between 0 and 4096.
- `Color` must be a Discord decimal color value between 0 and 16777215.
- Global and per-instance `ConcurrentRequests` must be between 1 and 100.

See the complete [configuration reference](FeedCord/docs/reference.md) for optional appearance settings, filters, and multi-instance examples.

### YouTube feeds

Direct YouTube Atom URLs are the most reliable option:

```text
https://www.youtube.com/feeds/videos.xml?channel_id=CHANNEL_ID
```

Channel-page URLs are also supported when the page exposes its feed link:

```json
{
  "YoutubeUrls": [
    "https://www.youtube.com/@ExampleChannel"
  ]
}
```

YouTube catch-up is limited to entries still present in the channel's Atom feed. FeedCord posts every available entry newer than its checkpoint, oldest first, but it cannot recover videos that YouTube has already removed from that feed window.

### Post filters

Plain filters match a post's title or description. Prefix a filter with `label:` to match an exact feed label. An exact URL filter takes precedence over an `"all"` filter.

```json
{
  "PostFilters": [
    {
      "Url": "https://github.com/Taeleus/FeedCord/releases.atom",
      "Filters": [
        "release",
        "label:security"
      ]
    },
    {
      "Url": "all",
      "Filters": [
        "announcement"
      ]
    }
  ]
}
```

Posts rejected by a filter are checkpointed without being sent, so they are not repeatedly reconsidered.

## Run with Docker

Pull the current stable image:

```bash
docker pull taeleus/feedcord:latest
```

Run FeedCord with a read-only configuration file and persistent state directory:

```bash
docker run --name feedcord \
  --restart unless-stopped \
  -v "/absolute/path/appsettings.json:/app/config/appsettings.json:ro" \
  -v "/absolute/path/feedcord-state:/app/state" \
  taeleus/feedcord:latest
```

Available image tags:

- `latest`: current `master` build
- `beta`: current `development` build
- Commit SHA tags for immutable deployments

The state mount is required if `PersistState` is enabled and checkpoints must survive container replacement.

The container runs as the non-root .NET application user and reports health from an internal heartbeat updated every 30 seconds.
Ensure the host state directory is writable by the container's application user (UID `1654` in the official .NET image).

## Run from source

```bash
git clone https://github.com/Taeleus/FeedCord.git
cd FeedCord
dotnet restore FeedCord.sln
dotnet run --project FeedCord -- /absolute/path/appsettings.json
```

If no configuration path is supplied, FeedCord looks for:

```text
FeedCord/bin/<configuration>/net10.0/config/appsettings.json
```

For local development, passing an explicit path is generally clearer.

## State and migration

Persistent delivery state is stored at:

```text
state/feed-state.json
```

The file contains separate checkpoints for each configured instance and is replaced atomically when saved.

If a legacy `feed_dump.csv` file exists and no JSON state file has been created, FeedCord imports the legacy timestamps. The next successful save writes the new JSON format.

If the JSON state file is malformed, FeedCord:

1. Logs the load failure.
2. Preserves a copy with a `.corrupt-<timestamp>` suffix.
3. Starts with an empty checkpoint set.

Starting with empty checkpoints intentionally avoids replaying the current contents of newly configured feeds.

## Discord behavior

- HTTP `429` responses honor Discord's `retry_after` value.
- FeedCord retries rate-limited webhook requests up to three times.
- A `400 Bad Request` triggers one text/forum payload fallback to detect an incorrect `Forum` setting.
- Webhook IDs and tokens are redacted from application-generated URL logs.
- Discord field lengths are clamped to API limits.
- Discord mentions from feed content are disabled by default.
- Invalid post, image, author, avatar, and footer URLs are omitted from payloads.

## Network safety

- Feed response bodies are limited to 10 MiB and must complete within 30 seconds.
- Image-page scraping accepts only public HTTP or HTTPS destinations.
- Redirects are handled explicitly and private-network redirect targets are rejected.

## Testing

Run the complete unit and fixture suite:

```bash
dotnet test FeedCord.sln --configuration Release
```

Run the same validation sequence used by CI:

```bash
dotnet restore FeedCord.sln
dotnet format FeedCord.sln --no-restore --verify-no-changes
dotnet build FeedCord.sln --configuration Release --no-restore -warnaserror
dotnet test FeedCord.sln --configuration Release --no-restore --no-build
```

The suite includes RSS, Atom, and YouTube fixtures; delivery and same-timestamp deduplication checks; rate-limit and cancellation tests; configuration validation; and concurrent/corrupt-state coverage.

### Optional Discord integration test

Set `FEEDCORD_TEST_DISCORD_WEBHOOK` to a disposable text-channel webhook before running the test suite:

```powershell
$env:FEEDCORD_TEST_DISCORD_WEBHOOK = "https://discord.com/api/webhooks/..."
dotnet test FeedCord.sln --configuration Release
```

```bash
export FEEDCORD_TEST_DISCORD_WEBHOOK="https://discord.com/api/webhooks/..."
dotnet test FeedCord.sln --configuration Release
```

The integration test posts one message. It does not print the webhook credential.

## Updating an existing installation

1. Stop the existing FeedCord process or container.
2. Back up `appsettings.json`, `feed_dump.csv`, and the `state` directory if present.
3. Replace the application or image.
4. Ensure `/app/state` is mounted for container deployments.
5. Start FeedCord and confirm that all configured instances pass validation.

The first run can migrate `feed_dump.csv`. Keep the backup until the new `state/feed-state.json` file has been created successfully.

Existing configurations that use `PersistenceOnShutdown` remain compatible. New configurations should use `PersistState`, which reflects that checkpoints are saved continuously after successful delivery as well as during graceful shutdown.

## Security notes

- Treat Discord webhook URLs as credentials.
- Do not commit `appsettings.json`.
- Prefer a read-only configuration mount.
- Restrict access to the state directory and application logs.
- Use a disposable webhook for integration testing.

## License

FeedCord is licensed under the [MIT License](LICENSE).
