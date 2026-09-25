# Hulaki.Discord

[Hulaki](https://github.com/sanamhub/hulaki) provider for Discord. Posts a message to a channel
through an incoming webhook, with the
[Execute Webhook](https://discord.com/developers/docs/resources/webhook#execute-webhook) call.

**Status: in development.** The API can change until 1.0.

```csharp
using Hulaki;
using Hulaki.Discord;

using var http = new HttpClient();
var discord = new DiscordChannel("ops", http, new DiscordChannelOptions { WebhookUrl = new Uri(webhookUrl) });
var outcome = await discord.SendAsync(new Message("Rain warning for Myagdi"), Recipient.Self);
```

| Fact | Value |
| --- | --- |
| Recipient | `Recipient.Self`: the webhook's own channel |
| Text limit | 2000 characters, counted in UTF-16 code units after escaping, title and link included |
| Markup | rendered as Discord Markdown; plain text is escaped so it shows as written |
| Mentions | off: `@everyone`, roles and users in the text never ping |
| Rate limit | 5 requests per 2 seconds per webhook by default; a 429's `retry_after` is honoured |

The webhook URL is a secret and is the request URI. Give the channel an `HttpClient` that does not
log request URIs; `IHttpClientFactory` logs them at Information by default.

Hulaki is not affiliated with Discord. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
