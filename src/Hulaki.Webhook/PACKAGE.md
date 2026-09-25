# Hulaki.Webhook

[Hulaki](https://github.com/sanamhub/hulaki) provider for any HTTP endpoint you control. Posts a
small JSON document, or a body built from your template, and can sign it so the receiver can
check where it came from.

**Status: in development.** The API can change until 1.0.

```csharp
using Hulaki;
using Hulaki.Webhook;

using var http = new HttpClient();
var hook = new WebhookChannel("hook", http, new WebhookChannelOptions
{
    Url = new Uri("https://alerts.example.org/hulaki"),
    Secret = sharedSecret,
});
var outcome = await hook.SendAsync(new Message("Rain warning for Myagdi") { Title = "Route alert" }, Recipient.Self);
```

The default body, with null fields left out:

```json
{ "title": "Route alert", "text": "Rain warning for Myagdi", "priority": "normal", "link": "https://example.org/" }
```

`priority` is `low`, `normal`, `high` or `urgent`. Markup is sent as plain text, with a link as
`text (url)`.

| Fact | Value |
| --- | --- |
| Recipient | `Recipient.Self`: the configured URL |
| Text limit | `MaxTextBytes`, 65536 UTF-8 bytes by default |
| Template | `BodyTemplate` with `{{title}}`, `{{text}}`, `{{priority}}`, `{{link}}`; values are escaped for a JSON string when `ContentType` is JSON, form-encoded for `application/x-www-form-urlencoded` |
| Signature | with `Secret` set: `X-Hulaki-Signature: sha256=<hex>`, the HMAC-SHA256 of the raw body |
| Outcome | 202 is `Accepted`, other 2xx `Delivered`; errors follow the HTTP status |

To verify a request, compute the HMAC-SHA256 of the raw body bytes with the shared secret, hex
encode it in lower case, prefix `sha256=`, and compare with the header using a constant-time
comparison (`CryptographicOperations.FixedTimeEquals` in .NET).

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
