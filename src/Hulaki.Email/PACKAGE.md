# Hulaki.Email

[Hulaki](https://github.com/sanamhub/hulaki) provider for email, through
[FreeTierMail](https://github.com/sanamhub/freetiermail).

**Status: in development.** The API can change until 1.0.

The channel works in one of two modes, set by `UseFreeTierMail`.

**One SMTP server** (the default). Set `Host`, `Port`, `Username` and `Password`:

```json
{
  "Hulaki": {
    "Channels": {
      "email": {
        "Host": "smtp.example.org",
        "Username": "alerts@example.org",
        "Password": "from user secrets",
        "From": "Route alerts <alerts@example.org>"
      }
    }
  }
}
```

**Through FreeTierMail.** Set `UseFreeTierMail` to true and configure the accounts once, in the
`FreeTierMail` section. The mailer picks an account by the quota each has left and fails over
when one is throttled, used up or down. Switch accounts per environment with `Enabled`, for
example a local [Mailpit](https://mailpit.axllent.org/) inbox in development and a paid Resend
plan in production:

```csharp
builder.Services.AddFreeTierMail(builder.Configuration.GetSection("FreeTierMail"))
    .AddResend()
    .AddSmtp("inbox");
builder.Services.AddHulaki()
    .AddEmail("email", builder.Configuration.GetSection("Hulaki:Channels:email"));
```

```json
{
  "FreeTierMail": {
    "Providers": {
      "resend": { "PreferForCritical": true },
      "inbox": { "Enabled": false }
    }
  },
  "Hulaki": {
    "Channels": {
      "email": { "UseFreeTierMail": true, "From": "Route alerts <alerts@example.org>" }
    }
  }
}
```

| Fact | Value |
| --- | --- |
| Recipient | an email address |
| SMTP transport | port 587 with STARTTLS required; 465 with TLS from the start; on a loopback host no login is needed and STARTTLS is used when offered |
| Body | a plain text part and an HTML part; markup is rendered into the HTML part |
| Subject | the title, or the first line of the text cut to 78 characters |
| Priority | `High` and `Urgent` are critical mail: providers marked `PreferForCritical` first, and the quota reserve |
| Outcome | `Accepted` with the provider's message id: the provider queued it, delivery happens later |

A refused recipient is `RecipientNotFound` and an address on the suppression list is
`RecipientBlocked`; neither is retried. Throttles, used-up quotas and outages are retried after a
delay, once the mailer has tried every account. When an answer was lost the outcome is `Unknown`
and nothing is resent.

`Password` and every provider key are secrets: keep them in user secrets or the environment.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
