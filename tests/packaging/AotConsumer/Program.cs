using System.Net;
using System.Text;
using Hulaki;
using Hulaki.Discord;
using Hulaki.Ntfy;
using Hulaki.Telegram;
using Hulaki.Webhook;

// One send per provider through HulakiClient, each answered by a stub with that platform's success
// body. Under Native AOT this exercises every provider's source-generated JSON, markup rendering,
// rate limiters and the retry pipeline.
using var http = new HttpClient(new PlatformStub());
using var telegram = new TelegramChannel("alerts", http, new TelegramChannelOptions
{
    BotToken = "123456:TEST-token_0000000000000000000",
});
using var discord = new DiscordChannel("discord", http, new DiscordChannelOptions
{
    WebhookUrl = new Uri("https://discord.com/api/webhooks/123456789012345678/TEST-webhook_token_0000000000"),
});
using var ntfy = new NtfyChannel("ntfy", http, new NtfyChannelOptions());
using var webhook = new WebhookChannel("webhook", http, new WebhookChannelOptions
{
    Url = new Uri("https://hooks.example.org/hulaki"),
    Secret = "TEST-shared-secret_000000",
});
var client = new HulakiClient([telegram, discord, ntfy, webhook]);

var message = new Message("**Orange** rain warning for Myagdi. [DHM](https://dhm.gov.np)")
{
    Format = TextFormat.Markup,
    Title = "Route alert",
    IdempotencyKey = "aot-consumer-1",
};
var result = await client.SendAsync(message,
[
    new Target("alerts", new Recipient("-1001234567890")),
    new Target("discord", Recipient.Self),
    new Target("ntfy", new Recipient("hulaki-aot-topic")),
    new Target("webhook", Recipient.Self),
]);

foreach (var (target, outcome) in result.Outcomes.Where(o => !o.Outcome.Succeeded))
{
    Console.Error.WriteLine($"{target.Channel}: {outcome}");
}

// CI compares the whole output with "Delivered".
Console.WriteLine(result.Status == SendStatus.Complete ? "Delivered" : "Failed");
return result.Status == SendStatus.Complete ? 0 : 1;

internal sealed class PlatformStub : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.RequestUri!.Host switch
        {
            "api.telegram.org" => """{"ok":true,"result":{"message_id":42}}""",
            "discord.com" => """{"id":"1100000000000000001"}""",
            "ntfy.sh" => """{"id":"sPs71M8A2T","event":"message"}""",
            "hooks.example.org" => "{}",
            _ => throw new InvalidOperationException("No stub for this host."),
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
