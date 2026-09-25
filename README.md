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

## How it avoids double sends

| Situation | Hulaki |
| --- | --- |
| Platform said "too many requests" or the connection was refused | retries: the platform did not act |
| Request sent, answer lost | reports `Unknown`; resends only if you set `ResendUnknown` |
| Platform deduplicates by key (Mastodon) | retries safely |
| You call again with the same `IdempotencyKey` | replays earlier outcomes, sends only what is missing |

## Credit

The outcome model, capability states and credential rotation are inspired by
[social-sdk.dev](https://social-sdk.dev) (MIT, TypeScript). No code is shared.

## License

MIT.
