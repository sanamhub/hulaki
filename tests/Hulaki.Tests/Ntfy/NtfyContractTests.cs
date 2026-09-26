using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Hulaki.Testing;

namespace Hulaki.Ntfy.Tests;

public sealed class NtfyContractTests : ChannelContractTests<NtfyChannel>
{
    protected override Recipient ValidRecipient { get; } = new(NtfyChannelTests.Topic);

    protected override NtfyChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("ntfy", new HttpClient(handler), new NtfyChannelOptions { TimeProvider = time });

    protected override HttpResponseMessage Success() =>
        new(HttpStatusCode.OK) { Content = new StringContent("""{"id":"sPs71M8A2T","event":"message"}""", Encoding.UTF8, "application/json") };
}
