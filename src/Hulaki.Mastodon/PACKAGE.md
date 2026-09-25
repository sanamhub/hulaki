# Hulaki.Mastodon

[Hulaki](https://github.com/sanamhub/hulaki) provider for Mastodon. Posts a status to the
account's timeline with [`POST /api/v1/statuses`](https://docs.joinmastodon.org/methods/statuses/#create).

**Status: in development.** The API can change until 1.0.

```csharp
using Hulaki;
using Hulaki.Mastodon;

using var http = new HttpClient();
var mastodon = new MastodonChannel("social", http, new MastodonChannelOptions
{
    InstanceUrl = new Uri("https://mastodon.social/"),
    AccessToken = accessToken,
    Visibility = "unlisted",
});
var outcome = await mastodon.SendAsync(new Message("Rain warning for Myagdi") { IdempotencyKey = "watch-42:rev-7" }, Recipient.Self);
```

| Fact | Value |
| --- | --- |
| Recipient | `Recipient.Self`: the account's timeline |
| Text limit | 500 characters, URLs counted as 23; set `MaxCharacters` for an instance that allows more |
| Instance limit | read from `/api/v2/instance` on the first send; a lower one is enforced before posting |
| Deduplication | `Idempotency-Key`: the message's key, or its fingerprint; retries after a lost answer are safe |
| Markup | sent as plain text, with a link as `text (url)` |
| Rate limit | 300 statuses per 3 hours by default; `X-RateLimit-Reset` is honoured on a 429 |

Without an `IdempotencyKey`, the key is the message's fingerprint, so posting the same text twice
within Mastodon's deduplication window returns the first status instead of a new one. Set a key
per post when a repeat is intended.

A refused token is `ReconnectRequired`. The access token is a secret.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
