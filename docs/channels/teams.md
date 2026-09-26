# Teams channel

Hulaki's channel for Microsoft Teams. Posts an Adaptive Card
to a channel or chat through a Workflows webhook.


In Teams, add the Workflows template "Post to a channel when a webhook request is received" and
copy its URL. Office 365 connector URLs (`outlook.office.com/webhook/...`,
`*.webhook.office.com/...`) are refused: Microsoft retired connectors on 2026-05-22.

```csharp
using Hulaki;
using Hulaki.Teams;

using var http = new HttpClient();
var teams = new TeamsChannel("ops", http, new TeamsChannelOptions { WorkflowUrl = new Uri(workflowUrl) });
var outcome = await teams.SendAsync(new Message("Rain warning for Myagdi") { Title = "Route alert" }, Recipient.Self);
```

| Fact | Value |
| --- | --- |
| Recipient | `Recipient.Self`: where the flow posts |
| Card | a bold title `TextBlock`, the text in a wrapping `TextBlock`, the link as an Open link button |
| Size limit | 28,000 bytes of card JSON |
| Markup | Adaptive Card Markdown: bold, italic, links; code spans are sent as text |
| Outcome | `Accepted`: Workflows answers 202 and posts the card itself |

The workflow URL carries a signature (`sig`) and is a secret. Give the channel an `HttpClient`
that does not log request URIs; `IHttpClientFactory` logs them at Information by default.

Hulaki is not affiliated with Microsoft. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
