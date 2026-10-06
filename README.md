# Hulaki

Deliver a message to people and accounts on chat, push and social platforms from .NET, with one
API and an honest answer about what happened to each one.

**Status: in development.** Nothing is published to nuget.org yet, and the API can change
until 1.0.

## What it is for

- Sending alerts to users on the channel each one chose: Telegram, Discord, ntfy, Web Push,
  email, Slack, Teams, a webhook.
- Posting to your own social accounts: Bluesky and Mastodon first.
- Knowing, per recipient, whether the message was delivered, rejected (and whether a retry can
  help), or lost in flight.

## What it is not for

Reading timelines, analytics, scheduling, campaign workflows or preference centres. Hulaki is a
delivery library.

## Limits, up front

- `net10.0` only.
- A platform can change its API, pricing or availability at any time. Hulaki's capability
  manifests say what each platform supports, charges for, or gates behind review.
- Some platforms cost money or need business verification: X, Viber, WhatsApp, Facebook Pages.
  They come after the free ones.

## Packages

| Package | What it adds |
| --- | --- |
| [`Hulaki`](src/Hulaki/PACKAGE.md) | core (messages, outcomes, retries, idempotency, diagnostics), `AddHulaki()`, and the channels below |
| [`Hulaki.Email`](src/Hulaki.Email/PACKAGE.md) | email through [FreeTierMail](https://github.com/sanamhub/freetiermail): one SMTP server, or any mix of SMTP and HTTP email accounts with failover |
| [`Hulaki.Testing`](src/Hulaki.Testing/PACKAGE.md) | fake channels, a recording handler and a contract test kit for providers |
| [`Hulaki.Cli`](tools/Hulaki.Cli/PACKAGE.md) | the `hulaki` tool: `capabilities`, `send`, `doctor` |

Channels in `Hulaki`, each with its own page:

| Channel | Namespace | What it sends through |
| --- | --- | --- |
| [Telegram](docs/channels/telegram.md) | `Hulaki.Telegram` | Telegram Bot API |
| [Discord](docs/channels/discord.md) | `Hulaki.Discord` | Discord webhooks |
| [ntfy](docs/channels/ntfy.md) | `Hulaki.Ntfy` | ntfy.sh or a self-hosted server |
| [Webhook](docs/channels/webhook.md) | `Hulaki.Webhook` | signed JSON to any HTTPS endpoint |
| [Web Push](docs/channels/webpush.md) | `Hulaki.WebPush` | browser push (VAPID, RFC 8291 encryption) |
| [Slack](docs/channels/slack.md) | `Hulaki.Slack` | Slack incoming webhooks |
| [Teams](docs/channels/teams.md) | `Hulaki.Teams` | Teams through a Workflows webhook |
| [Bluesky](docs/channels/bluesky.md) | `Hulaki.Bluesky` | Bluesky posts and threads |
| [Mastodon](docs/channels/mastodon.md) | `Hulaki.Mastodon` | Mastodon statuses |

Only email is a separate package, because it is the one channel with dependencies (FreeTierMail
and MailKit). The channels you never construct are trimmed from a Native AOT app.

Every `Add<Name>(name, section)` call skips the channel when the section has `"Enabled": false`,
so one registration serves every environment and `appsettings.{Environment}.json` turns channels
on and off.

What each platform supports, limits and charges for is in [docs/capabilities.md](docs/capabilities.md),
generated from the providers' manifests.

## Install

Not on nuget.org yet. Once it is:

```
dotnet add package Hulaki
dotnet add package Hulaki.Email   # only for email
```

## Send a message

```csharp
using Hulaki;
using Hulaki.Telegram;

using var http = new HttpClient();
using var telegram = new TelegramChannel("alerts", http, new TelegramChannelOptions
{
    BotToken = configuration["Hulaki:Channels:alerts:BotToken"]!,
});
var client = new HulakiClient([telegram]);

var message = new Message("**Orange** rain warning for Myagdi on 26 Sep. [DHM](https://dhm.gov.np)")
{
    Format = TextFormat.Markup,
    Title = "Route alert: Pokhara to Beni",
    Priority = MessagePriority.High,
    IdempotencyKey = "watch-42:2026-09-26:rev-7",
};

var result = await client.SendAsync(message, [new Target("alerts", new Recipient("123456789"))]);
foreach (var (target, outcome) in result.Outcomes)
{
    switch (outcome.Error?.Code)
    {
        case null: break;                                   // delivered or accepted
        case HulakiErrorCode.RecipientBlocked:
        case HulakiErrorCode.RecipientNotFound: /* disable this subscription */ break;
        case HulakiErrorCode.RateLimited: /* reschedule after outcome.Error.RetryAfter */ break;
        default: /* log outcome.Error.Code; never the recipient */ break;
    }
}
```

With dependency injection:

```csharp
builder.Services.AddHulaki()
    .AddTelegram("alerts", builder.Configuration.GetSection("Hulaki:Channels:alerts"))
    .AddNtfy("ops", o => o.BaseAddress = new Uri("https://ntfy.sh/"));
```

Both samples are compiled by CI from [samples/Hulaki.Samples](samples/Hulaki.Samples).

## How it avoids double sends

| Situation | Hulaki |
| --- | --- |
| Platform said "too many requests" or the connection was refused | retries: the platform did not act |
| Request sent, answer lost | reports `Unknown`; resends only if you set `ResendUnknown` |
| Platform deduplicates by key (Mastodon) | retries safely |
| You call again with the same `IdempotencyKey` | replays earlier outcomes, sends only what is missing |

## Security notes

- Telegram puts the bot token in the request path, and Discord and Slack webhook URLs are
  secrets in the path. Each provider's `Add…` registration (`AddTelegram`, `AddDiscord`, and so
  on) removes the loggers from its `HttpClient`. If you build the `HttpClient` yourself, do not
  log its request URIs.
- OpenTelemetry's HTTP client spans record `url.full`. .NET redacts the query string there, not
  the path, so leave these hosts out of HTTP client tracing. Hulaki's own `Hulaki` spans carry no
  URL, recipient or message text.

  ```csharp
  string[] secretInPathHosts = ["api.telegram.org", "discord.com", "hooks.slack.com"];
  Sdk.CreateTracerProviderBuilder()
      .AddSource("Hulaki")
      .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage =
          request => request.RequestUri is not { } uri || !secretInPathHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
      .Build();
  ```

- `Recipient.ToString()` prints `Recipient(***)`, and `HulakiError.Message` is Hulaki's own text,
  never the platform's response body.
- Report a vulnerability as [SECURITY.md](SECURITY.md) describes, not in a public issue.

## Credit

The outcome model, capability states and credential rotation are inspired by
[social-sdk.dev](https://social-sdk.dev) (MIT, TypeScript). No code is shared.

## License

MIT.
