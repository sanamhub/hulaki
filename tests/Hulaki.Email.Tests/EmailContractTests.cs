using System;
using System.Net;
using System.Net.Http;
using Hulaki.Testing;

namespace Hulaki.Email.Tests;

/// <summary>The contract kit over SMTP, through <see cref="HttpScriptedSmtpSession"/>.</summary>
public sealed class EmailContractTests : ChannelContractTests<EmailChannel>
{
    protected override Recipient ValidRecipient { get; } = new(EmailChannelTests.To);

    protected override EmailChannel Create(HttpMessageHandler handler, TimeProvider time)
    {
        var http = new HttpClient(handler);
        return new EmailChannel(
            "mail",
            new EmailChannelOptions { Host = "smtp.example.org", From = "alerts@example.org", TimeProvider = time },
            () => new HttpScriptedSmtpSession(http));
    }

    protected override HttpResponseMessage Success() => new(HttpStatusCode.OK);
}
