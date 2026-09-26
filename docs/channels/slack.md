# Slack channel

Hulaki's channel for Slack. Posts a message to a channel
through an [incoming webhook](https://api.slack.com/messaging/webhooks).


```csharp
using Hulaki;
using Hulaki.Slack;

using var http = new HttpClient();
var slack = new SlackChannel("ops", http, new SlackChannelOptions { WebhookUrl = new Uri(webhookUrl) });
var outcome = await slack.SendAsync(new Message("Rain warning for Myagdi"), Recipient.Self);
```

| Fact | Value |
| --- | --- |
| Recipient | `Recipient.Self`: the webhook's own channel |
| Text limit | 40,000 characters, counted in UTF-16 code units after escaping; Slack advises under 4,000 |
| Markup | mrkdwn: `*bold*`, `_italic_`, `` `code` ``, `<url\|text>`; `&`, `<` and `>` are escaped |
| Rate limit | 1 message per second per webhook by default; a 429's `Retry-After` is honoured |
| Errors | `invalid_payload` is `InvalidInput`, `channel_not_found` and `channel_is_archived` are `RecipientNotFound`, `no_service` is `InvalidConfiguration` |

Slack has no escape for `*`, `_` or `~`, so plain text containing them can render as formatting.

The webhook URL is a secret and is the request URI. Give the channel an `HttpClient` that does not
log request URIs; `IHttpClientFactory` logs them at Information by default.

Hulaki is not affiliated with Slack. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
