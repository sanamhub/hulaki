# Ntfy channel

Hulaki's channel for [ntfy](https://ntfy.sh). Publishes a
message to a topic on ntfy.sh or a self-hosted server with the
[JSON publish](https://docs.ntfy.sh/publish/#publish-as-json) call.


```csharp
using Hulaki;
using Hulaki.Ntfy;

using var http = new HttpClient();
var ntfy = new NtfyChannel("ops", http, new NtfyChannelOptions());
var outcome = await ntfy.SendAsync(
    new Message("Rain warning for Myagdi") { Title = "Route alert", Priority = MessagePriority.High },
    new Recipient("my-random-topic-name"));
```

| Fact | Value |
| --- | --- |
| Recipient | the topic name: 1 to 64 letters, digits, `-` or `_` |
| Text limit | 4096 UTF-8 bytes of body; the title is a separate field |
| Markup | sent as Markdown with `markdown: true` |
| Priority | Low 2, Normal 3, High 4, Urgent 5 |
| Link | `Message.Link` is the click action |
| Rate limit | on ntfy.sh, a burst of 60 then 1 every 5 seconds; none for other servers |

Anyone who knows a topic name on ntfy.sh can read it. Use a long random name, or an access token
with a reserved topic. `AccessToken` is sent as a bearer token and is a secret.

Hulaki is not affiliated with ntfy. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
