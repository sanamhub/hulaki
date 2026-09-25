# Hulaki.WebPush

[Hulaki](https://github.com/sanamhub/hulaki) provider for Web Push. Sends to a browser push
subscription with [VAPID](https://www.rfc-editor.org/rfc/rfc8292) and
[aes128gcm payload encryption](https://www.rfc-editor.org/rfc/rfc8291), using only
`System.Security.Cryptography`. It reaches a phone or desktop browser without an account on any
third-party platform.

**Status: in development.** The API can change until 1.0.

```csharp
using Hulaki;
using Hulaki.WebPush;

using var http = new HttpClient();
var push = new WebPushChannel("push", http, new WebPushChannelOptions
{
    VapidPublicKey = vapidPublicKey,   // base64url, 65 bytes
    VapidPrivateKey = vapidPrivateKey, // base64url, 32 bytes
    VapidSubject = "mailto:ops@example.org",
});

// From the browser's PushSubscription.toJSON(): endpoint and keys.
var subscriber = new Recipient(endpoint, new Dictionary<string, string> { ["p256dh"] = p256dh, ["auth"] = auth });
var outcome = await push.SendAsync(new Message("Rain warning for Myagdi") { Title = "Route alert" }, subscriber);
```

The service worker receives this JSON, with null fields left out:

```json
{ "title": "Route alert", "body": "Rain warning for Myagdi", "url": "https://example.org/watch/42" }
```

| Fact | Value |
| --- | --- |
| Recipient | the subscription endpoint, with `p256dh` and `auth` in `Properties` |
| Payload limit | 3993 UTF-8 bytes of JSON: one 4096-byte record after encryption |
| Priority | `Urgency` header: Low `very-low`, Normal `normal`, High and Urgent `high` |
| Time to live | `TimeToLive`, 24 hours by default |
| Outcome | `Accepted`: the push service queued it |
| Expired subscription | 404 or 410 is `RecipientNotFound`: delete the subscription |

The VAPID private key is a secret. Endpoints are capability URLs: anyone holding one with its keys
can push to that browser, so keep them out of logs.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
