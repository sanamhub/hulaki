using System;
using System.Net;
using System.Net.Http;
using Hulaki.Testing;

namespace Hulaki.WebPush.Tests;

public sealed class WebPushContractTests : ChannelContractTests<WebPushChannel>
{
    protected override Recipient ValidRecipient { get; } = Rfc8291.Subscriber();

    protected override WebPushChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("push", new HttpClient(handler), new WebPushChannelOptions
        {
            VapidPublicKey = VapidKeys.Shared.PublicKey,
            VapidPrivateKey = VapidKeys.Shared.PrivateKey,
            VapidSubject = "mailto:ops@example.org",
            TimeProvider = time,
        });

    protected override HttpResponseMessage Success() => new(HttpStatusCode.Created);
}
