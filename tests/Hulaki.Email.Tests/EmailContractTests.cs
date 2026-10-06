using System;
using System.Net;
using System.Net.Http;
using FreeTierMail;
using Hulaki.Testing;

namespace Hulaki.Email.Tests;

/// <summary>The contract kit over FreeTierMail, through <see cref="ScriptedHttpEmailProvider"/>.</summary>
public sealed class EmailContractTests : ChannelContractTests<EmailChannel>
{
    protected override Recipient ValidRecipient { get; } = new(EmailChannelTests.To);

    protected override EmailChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new(
            "mail",
            new FreeTierMailer([new ScriptedHttpEmailProvider(new HttpClient(handler))], new FreeTierMailerOptions { TimeProvider = time }),
            new EmailChannelOptions { UseFreeTierMail = true, From = "alerts@example.org", TimeProvider = time });

    protected override HttpResponseMessage Success() => new(HttpStatusCode.OK);
}
