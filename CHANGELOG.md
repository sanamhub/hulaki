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
- `AddHulaki()` dependency injection, `Hulaki.Testing` (contract kit) and the `hulaki` tool.
- Three packages: `Hulaki` (core, `AddHulaki()` and every channel without an extra dependency),
  `Hulaki.Email` (FreeTierMail and MailKit) and `Hulaki.Testing`. Channel namespaces are unchanged
  (`Hulaki.Telegram`, `Hulaki.Discord` and so on).
- `Enabled` in a channel's configuration section: false leaves the channel out, so one
  `Add<Name>(name, section)` call serves every environment.
- `Hulaki.Email` sends through FreeTierMail. By default it uses the one SMTP server in its options,
  as before; with `UseFreeTierMail` it sends through the `FreeTierMailer` registered with
  `AddFreeTierMail()`, so any mix of SMTP and HTTP email accounts (Resend, Brevo and others) with
  quota-aware failover. High and Urgent messages are FreeTierMail's critical mail. An SMTP
  server that is not on loopback now needs a login.
- README samples live in `samples/Hulaki.Samples`, which CI builds, so they cannot drift from the
  API.
