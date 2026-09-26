# Hulaki

Deliver a message to people and accounts on chat, push and social platforms from .NET, with one
API and an outcome per recipient. [Source and docs](https://github.com/sanamhub/hulaki).

**Status: in development.** The API can change until 1.0.

This package holds the core (message model, retry rules, idempotency, text counting, markup),
every channel that needs no extra dependency, and `AddHulaki()` for dependency injection. Email
over SMTP brings MailKit, so it is a separate package,
[`Hulaki.Email`](https://www.nuget.org/packages/Hulaki.Email).

| Channel | Type | Docs |
| --- | --- | --- |
| Telegram Bot API | `Hulaki.Telegram.TelegramChannel` | [telegram](https://github.com/sanamhub/hulaki/blob/main/docs/channels/telegram.md) |
| Discord webhook | `Hulaki.Discord.DiscordChannel` | [discord](https://github.com/sanamhub/hulaki/blob/main/docs/channels/discord.md) |
| Slack incoming webhook | `Hulaki.Slack.SlackChannel` | [slack](https://github.com/sanamhub/hulaki/blob/main/docs/channels/slack.md) |
| Teams Workflows webhook | `Hulaki.Teams.TeamsChannel` | [teams](https://github.com/sanamhub/hulaki/blob/main/docs/channels/teams.md) |
| ntfy | `Hulaki.Ntfy.NtfyChannel` | [ntfy](https://github.com/sanamhub/hulaki/blob/main/docs/channels/ntfy.md) |
| Web Push (VAPID, RFC 8291) | `Hulaki.WebPush.WebPushChannel` | [webpush](https://github.com/sanamhub/hulaki/blob/main/docs/channels/webpush.md) |
| Signed JSON webhook | `Hulaki.Webhook.WebhookChannel` | [webhook](https://github.com/sanamhub/hulaki/blob/main/docs/channels/webhook.md) |
| Bluesky | `Hulaki.Bluesky.BlueskyChannel` | [bluesky](https://github.com/sanamhub/hulaki/blob/main/docs/channels/bluesky.md) |
| Mastodon | `Hulaki.Mastodon.MastodonChannel` | [mastodon](https://github.com/sanamhub/hulaki/blob/main/docs/channels/mastodon.md) |

```csharp
builder.Services.AddHulaki()
    .AddTelegram("alerts", builder.Configuration.GetSection("Hulaki:Channels:alerts"));
```

`AddHulaki()` registers `HulakiClient` as a singleton over every registered channel, and an
in-memory idempotency store unless you register your own `IIdempotencyStore`. Each channel gets
options bound with the configuration binding generator (Native AOT safe), validation at startup,
and a named `HttpClient` (`hulaki.<name>`) with a 30 second timeout and no request logging,
because some platforms put the secret in the URL path. Do not add a retry or resilience handler
to these clients: it would resend requests the platform may already have acted on.

- `net10.0` only, trim and Native AOT compatible. Channels you never construct are trimmed away.
- Retries only where the platform provably did not act. A lost answer is reported as `Unknown`.
- No token, recipient address or message text in logs, traces or error messages.

Hulaki is not affiliated with any of these platforms. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
