using System;
using System.Net;
using System.Net.Http;
using Hulaki.Testing;

namespace Hulaki.Webhook.Tests;

public sealed class WebhookContractTests : ChannelContractTests<WebhookChannel>
{
    protected override Recipient ValidRecipient => Recipient.Self;

    protected override WebhookChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("hook", new HttpClient(handler), new WebhookChannelOptions { Url = new Uri(WebhookChannelTests.Url), TimeProvider = time });

    protected override HttpResponseMessage Success() => new(HttpStatusCode.NoContent);
}
