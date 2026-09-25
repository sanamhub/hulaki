# Hulaki

The core package of [Hulaki](https://github.com/sanamhub/hulaki): deliver a message to people and
accounts on chat, push and social platforms from .NET, with one API and an outcome per recipient.

**Status: in development.** The API can change until 1.0.

Install a provider package, such as `Hulaki.Telegram`, to send anywhere. This package holds the
parts every provider shares: the message model, retry rules, idempotency, text counting and markup.

- `net10.0` only, trim and Native AOT compatible.
- Retries only where the platform provably did not act. A lost answer is reported as `Unknown`.
- No token, recipient address or message text in logs, traces or error messages.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
