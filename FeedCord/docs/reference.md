# FeedCord configuration reference

FeedCord reads configuration from `appsettings.json`. Configuration is loaded once during startup; restart FeedCord after changing the file.

## Complete example

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
      "ConcurrentRequests": 5,
      "Username": "Engineering News",
      "AvatarUrl": "https://example.com/feedcord-avatar.png",
      "AuthorName": null,
      "AuthorUrl": null,
      "AuthorIcon": null,
      "FallbackImage": "https://example.com/fallback.png",
      "FooterImage": "https://example.com/footer.png",
      "PostFilters": [
        {
          "Url": "https://github.com/Taeleus/FeedCord/releases.atom",
          "Filters": [
            "release",
            "label:security"
          ]
        }
      ]
    }
  ],
  "ConcurrentRequests": 20
}
```

## Top-level properties

| Property | Required | Default | Valid values | Description |
| --- | --- | --- | --- | --- |
| `Instances` | Yes | None | One or more instance objects | Each instance runs an independent feed worker and publishes to one Discord webhook. `Id` values must be unique, ignoring letter case. |
| `ConcurrentRequests` | No | `20` | `1`–`100` | Maximum simultaneous HTTP requests across every instance. |

An empty `Instances` array is invalid.

## Instance properties

### Feed and delivery settings

| Property | Required | Default | Valid values | Description |
| --- | --- | --- | --- | --- |
| `Id` | Yes | None | Non-empty unique string | Identifies the instance in logs and persisted state. IDs are compared case-insensitively. |
| `RssUrls` | Yes | None | Array of absolute HTTP/HTTPS URLs | RSS or Atom feeds. Use `[]` when unused. |
| `YoutubeUrls` | Yes | None | Array of absolute HTTP/HTTPS URLs | YouTube channel pages or direct YouTube Atom feed URLs. Use `[]` when unused. |
| `DiscordWebhookUrl` | Yes | None | HTTPS Discord webhook URL | Destination webhook. Treat this value as a credential. |
| `RssCheckIntervalMinutes` | Yes | `0`, which fails validation | `1`–`1440` | Delay between completed feed-check cycles. |
| `DescriptionLimit` | Yes | `0` | `0`–`4096` | Maximum post-description length. `0` leaves parsed descriptions untrimmed; Discord payloads are still capped at API limits. |
| `Forum` | No | `false` | Boolean | Use Discord forum-thread payloads when `true`; use text-channel payloads when `false`. |
| `MarkdownFormat` | No | `false` | Boolean | Send a Markdown content message instead of a Discord embed. |
| `PersistState` | No | `false` | Boolean | Save delivery checkpoints after every accepted or filtered post and during graceful shutdown. |
| `EnableAutoRemove` | No | `false` | Boolean | Remove a feed from the running instance after three consecutive fetch or parse failures. Removal lasts until FeedCord restarts. |
| `ConcurrentRequests` | No | `5` | `1`–`100` | Maximum simultaneous feed operations within this instance. The top-level request limit still applies. |

Both URL arrays must exist, and at least one non-empty feed URL must be configured across them.

`PersistenceOnShutdown` remains accepted as a legacy alias for `PersistState`. New configurations should use `PersistState`.

### Discord appearance settings

| Property | Required | Default | Description |
| --- | --- | --- | --- |
| `Username` | No | `FeedCord` | Webhook display name. Payloads are limited to Discord's 80-character username limit. |
| `AvatarUrl` | No | None | HTTP/HTTPS webhook avatar URL. Invalid schemes are omitted. |
| `AuthorName` | No | Feed author | Overrides the author name displayed in embeds. |
| `AuthorUrl` | No | None | HTTP/HTTPS link attached to the embed author. Invalid schemes are omitted. |
| `AuthorIcon` | No | None | HTTP/HTTPS icon URL for the embed author. Invalid schemes are omitted. |
| `FallbackImage` | No | None | HTTP/HTTPS image used when FeedCord cannot extract a post image. |
| `FooterImage` | No | None | HTTP/HTTPS icon displayed in the embed footer. |
| `Color` | No | `0` | Decimal Discord embed color from `0` through `16777215`. |

Feed-supplied Discord mentions are disabled. Invalid post, image, author, avatar, and footer URLs are omitted from webhook payloads.

## Multiple instances

Each instance can use separate feeds, formatting, request limits, persistence behavior, and webhook destinations.

```json
{
  "Instances": [
    {
      "Id": "Gaming",
      "RssUrls": [
        "https://example.com/gaming.xml"
      ],
      "YoutubeUrls": [],
      "DiscordWebhookUrl": "https://discord.com/api/webhooks/GAMING_ID/GAMING_TOKEN",
      "RssCheckIntervalMinutes": 10,
      "DescriptionLimit": 500,
      "Color": 3447003,
      "Forum": false,
      "MarkdownFormat": false,
      "PersistState": true,
      "ConcurrentRequests": 3
    },
    {
      "Id": "Security",
      "RssUrls": [
        "https://example.com/security.xml"
      ],
      "YoutubeUrls": [
        "https://www.youtube.com/feeds/videos.xml?channel_id=CHANNEL_ID"
      ],
      "DiscordWebhookUrl": "https://discord.com/api/webhooks/SECURITY_ID/SECURITY_TOKEN",
      "RssCheckIntervalMinutes": 5,
      "DescriptionLimit": 1000,
      "Color": 15158332,
      "Forum": true,
      "MarkdownFormat": false,
      "PersistState": true,
      "ConcurrentRequests": 5
    }
  ],
  "ConcurrentRequests": 20
}
```

## Post filters

`PostFilters` is optional. Each filter object contains:

| Property | Required | Description |
| --- | --- | --- |
| `Url` | Yes | Exact configured feed URL, or `"all"` for a fallback filter. |
| `Filters` | Yes | One or more non-empty filter strings. |

Plain filters match the post title or description, case-insensitively. Prefix a filter with `label:` to match an exact feed label:

```json
{
  "PostFilters": [
    {
      "Url": "https://example.com/releases.atom",
      "Filters": [
        "release",
        "security fix",
        "label:critical"
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

Filter behavior:

- An exact URL filter takes precedence over `"all"`.
- A post is included when any configured filter matches.
- If no exact or `"all"` filter applies, the post is included.
- Filtered posts are checkpointed without being sent and are not reconsidered during later cycles.

## YouTube feeds and catch-up

Direct YouTube Atom feeds are the most reliable:

```text
https://www.youtube.com/feeds/videos.xml?channel_id=CHANNEL_ID
```

Channel-page URLs are supported when the page exposes an RSS link.

FeedCord processes every entry present in the Atom feed that is newer than the persisted checkpoint and posts missed videos oldest first. Catch-up is limited to entries YouTube still exposes in that feed; FeedCord cannot recover older videos that have already fallen outside YouTube's feed window.

## Persistence

When `PersistState` is enabled, FeedCord stores state at:

```text
state/feed-state.json
```

For Docker deployments, mount `/app/state` to persistent host storage:

```bash
-v "/absolute/path/feedcord-state:/app/state"
```

The state directory must be writable by the container application user, UID `1654` in the official .NET image.

State behavior:

- A checkpoint is saved after each successfully delivered post.
- Filtered posts are also checkpointed.
- Failed Discord deliveries are not checkpointed and are retried.
- Publication times and stable item IDs prevent posts sharing a timestamp from being skipped.
- State writes use an atomic temporary-file replacement.
- Existing `feed_dump.csv` timestamps are imported when no JSON state exists.
- A malformed JSON state file is preserved with a `.corrupt-<timestamp>` suffix before FeedCord starts with empty state.

## Reserved configuration

`Pings` exists in the current configuration model but is not implemented. It has no effect and should be omitted.

## Validation summary

FeedCord refuses to start when:

- No instances are configured.
- Two instance IDs differ only by letter case.
- Both feed arrays contain no usable URLs.
- A feed URL is not an absolute HTTP or HTTPS URL.
- The Discord webhook is not a valid HTTPS Discord webhook URL.
- An interval, request limit, description limit, or color is outside its documented range.
- A post-filter URL or filter value is empty.

Configuration is loaded only during startup. Restart FeedCord after modifying `appsettings.json`.
