using System;
using System.Net;
using System.Net.Http;
using System.Text;
using Hulaki.Testing;

namespace Hulaki.Telegram.Tests;

public sealed class TelegramContractTests : ChannelContractTests<TelegramChannel>
{
    protected override Recipient ValidRecipient { get; } = new("-1001234567890");

    protected override TelegramChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("tg", new HttpClient(handler), new TelegramChannelOptions { BotToken = "123456:TEST-token_0000000000000000000", TimeProvider = time });

    protected override HttpResponseMessage Success() =>
        new(HttpStatusCode.OK) { Content = new StringContent("""{"ok":true,"result":{"message_id":1}}""", Encoding.UTF8, "application/json") };
}
