# Hulaki.Cli

The `hulaki` command for [Hulaki](https://github.com/sanamhub/hulaki). It prints what each
provider supports, sends a test message, and checks a channel's configuration without sending.

**Status: in development.** Commands and URL shapes can change until 1.0.

```bash
dotnet tool install --global Hulaki.Cli --prerelease
hulaki capabilities
hulaki doctor --channel-url "$HULAKI_CHANNEL"
hulaki send --channel-url "$HULAKI_CHANNEL" --to 123456789 --text "Test from hulaki"
```

| Command | What it does | Exit code |
| --- | --- | --- |
| `capabilities [--markdown]` | prints every provider's capability manifest | 0 |
| `doctor --channel-url <url>` | builds the channel and runs its startup checks; makes no request | 0 valid, 2 invalid |
| `send --channel-url <url> [--to <address>] --text <text> [--title <title>] [--property key=value]` | sends one message and prints the outcome | 0 delivered or accepted, 1 failed or unknown, 2 bad input |

Channel URLs put the secret in the URL, so they exist for this tool only; applications configure
channels with typed options. Output shows a channel as `scheme://host` and never prints the secret,
the recipient or the text. Keep the URL in an environment variable so it stays out of shell history.

| Provider | Channel URL |
| --- | --- |
| Telegram | `telegram://<bot-token>@telegram[?base=<bot api url>]` |
| Discord | `discord://<webhook-id>:<webhook-token>@discord` |
| ntfy | `ntfy://[<access-token>@]<host>[:port][/path]`, HTTP only for a loopback host |
| Slack | `slack://hooks.slack.com/services/<T...>/<B...>/<secret>` |
| Teams | `teams://<workflow host>/<path>?<query>`: the Workflows URL with `teams` as its scheme |
| Webhook | `webhook+https://[<hmac-secret>@]<host>/<path>` |
| Email | `smtp://[<user>[:<password>]@]<host>[:port]?from=<address>`, port 587 by default |
| Web Push | `webpush://<vapid-public-key>:<vapid-private-key>@vapid?subject=<mailto:...>[&ttl=<seconds>]`; pass `--property p256dh=... --property auth=...` with the endpoint as `--to` |

Percent-encode reserved characters in a secret, for example `@` as `%40`.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
