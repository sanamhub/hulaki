# Hulaki.Telegram

[Hulaki](https://github.com/sanamhub/hulaki) provider for Telegram. Sends a message to a chat id or
`@channelusername` through the Bot API
[`sendMessage`](https://core.telegram.org/bots/api#sendmessage) method.

**Status: in development.** The API can change until 1.0.

```csharp
using Hulaki;
using Hulaki.Telegram;

using var http = new HttpClient();
var telegram = new TelegramChannel("alerts", http, new TelegramChannelOptions { BotToken = botToken });
var outcome = await telegram.SendAsync(new Message("Rain warning for Myagdi"), new Recipient("123456789"));
```

| Fact | Value |
| --- | --- |
| Text limit | 4096 UTF-16 code units, title and link included |
| Markup | rendered as Telegram HTML |
| Rate limits | 30 messages per second per bot, 1 per second per chat ([Bot FAQ](https://core.telegram.org/bots/faq)) |
| Blocked or removed recipient | `RecipientBlocked`: stop sending to them |

The bot token is part of every request path. Give the channel an `HttpClient` that does not log
request URIs; `IHttpClientFactory` logs them at Information by default.

Hulaki is not affiliated with Telegram. Source, issues and license (MIT):
https://github.com/sanamhub/hulaki
