using System.Net;
using System.Text;
using Hulaki;
using Hulaki.Telegram;

// One Telegram send through HulakiClient, answered by a stub. Under Native AOT this exercises the
// source-generated JSON, markup rendering, the rate limiters and the retry pipeline.
using var http = new HttpClient(new TelegramSuccess());
using var telegram = new TelegramChannel("alerts", http, new TelegramChannelOptions
{
    BotToken = "123456:TEST-token_0000000000000000000",
});
var client = new HulakiClient([telegram]);

var message = new Message("**Orange** rain warning for Myagdi. [DHM](https://dhm.gov.np)")
{
    Format = TextFormat.Markup,
    Title = "Route alert",
    IdempotencyKey = "aot-consumer-1",
};
var result = await client.SendAsync(message, [new Target("alerts", new Recipient("-1001234567890"))]);
var outcome = result.Outcomes[0].Outcome;

Console.WriteLine(outcome.Status);
return outcome.Status == DeliveryStatus.Delivered ? 0 : 1;

internal sealed class TelegramSuccess : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true,"result":{"message_id":42}}""", Encoding.UTF8, "application/json"),
        });
}
