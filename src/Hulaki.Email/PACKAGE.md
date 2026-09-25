# Hulaki.Email

[Hulaki](https://github.com/sanamhub/hulaki) provider for email. Sends through an SMTP submission
server with [MailKit](https://github.com/jstedfast/MailKit), one connection per send.

**Status: in development.** The API can change until 1.0.

```csharp
using Hulaki;
using Hulaki.Email;

var email = new EmailChannel("mail", new EmailChannelOptions
{
    Host = "smtp.example.org",
    Username = "alerts@example.org",
    Password = smtpPassword,
    From = "Route alerts <alerts@example.org>",
});
var outcome = await email.SendAsync(new Message("Rain warning for Myagdi") { Title = "Route alert" }, new Recipient("someone@example.org"));
```

| Fact | Value |
| --- | --- |
| Recipient | an email address |
| Transport | port 587 with STARTTLS required by default; 465 with TLS from the start; on a loopback host STARTTLS is used when offered |
| Body | a plain text part and an HTML part; markup is rendered into the HTML part |
| Subject | the title, or the first line of the text cut to 78 characters |
| Priority | `Priority`, `Importance` and `X-Priority` headers |
| Outcome | `Accepted` with the Message-Id: the server queued it, delivery happens later |

SMTP replies map to outcomes by class. A 4xx reply is retried after a delay. A 550, 551 or 553 for
the recipient is `RecipientNotFound`. Other 5xx replies are not retried. A connection that breaks
while the message is being sent is `Unknown`, because the server may have queued it.

XOAUTH2 is not supported yet. `Password` is a secret.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
