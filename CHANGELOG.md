# Changelog

Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
Versioning follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Hulaki` core: `Message`, `Recipient`, `HulakiClient` with per-target outcomes in input order,
  the retry table (a request the platform may have acted on is reported `Unknown`, not resent),
  idempotency keys, credential refresh, and `Hulaki` traces and metrics.
- Providers: Telegram, Discord, ntfy, Webhook, Email (SMTP), Web Push, Slack, Teams, Bluesky and
  Mastodon, each with a capability manifest. `docs/capabilities.md` is generated from them.
- `Hulaki.Extensions.DependencyInjection`, `Hulaki.Testing` (contract kit) and the `hulaki` tool.
- README samples live in `samples/Hulaki.Samples`, which CI builds, so they cannot drift from the
  API.
