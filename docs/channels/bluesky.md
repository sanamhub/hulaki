# Bluesky channel

Hulaki's channel for [Bluesky](https://bsky.app). Posts to
the account's feed with an app password.


```csharp
using Hulaki;
using Hulaki.Bluesky;

using var http = new HttpClient();
var bluesky = new BlueskyChannel("social", http, new BlueskyChannelOptions
{
    Identifier = "alerts.example.org",
    AppPassword = appPassword,
    Languages = { "ne", "en" },
});
var outcome = await bluesky.SendAsync(new Message("Rain warning for Myagdi. https://example.org/watch/42"), Recipient.Self);
// outcome.Url is the post on bsky.app
```

| Fact | Value |
| --- | --- |
| Recipient | `Recipient.Self`: the account's feed |
| Text limit | 300 graphemes, counted the way Bluesky does, Devanagari conjuncts included |
| Longer text | refused, or posted as a thread of replies with `ThreadLongPosts` |
| Links | markup links, bare `http` and `https` URLs and `Message.Link` become link facets |
| Markup | bold, italic and code are sent as plain text; Bluesky has no formatting |
| Session | created once with `createSession`, kept in `CredentialStore`, refreshed before it expires and after an `ExpiredToken` |
| Rate limit | 27 posts a minute by default, under Bluesky's 5,000 points an hour at 3 a post |

A refused app password is `ReconnectRequired`: a person has to issue a new one. A thread that fails
after its first part is `Failed` with `ReconcileFirst` and is not retried, because a retry would
post the first parts again.

The app password is a secret, and the stored session tokens are too. OAuth is not supported yet.

Hulaki is not affiliated with Bluesky. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
