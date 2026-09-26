using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Hulaki.Testing;

namespace Hulaki.Discord.Tests;

public sealed class DiscordContractTests : ChannelContractTests<DiscordChannel>
{
    protected override Recipient ValidRecipient => Recipient.Self;

    protected override DiscordChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("discord", new HttpClient(handler), new DiscordChannelOptions { WebhookUrl = new Uri(DiscordChannelTests.Webhook), TimeProvider = time });

    protected override HttpResponseMessage Success() =>
        new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"1100000000000000001"}""", Encoding.UTF8, "application/json") };
}
