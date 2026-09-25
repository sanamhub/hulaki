using System.Buffers.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Hulaki;
using Hulaki.Bluesky;
using Hulaki.Channels;
using Hulaki.Discord;
using Hulaki.Email;
using Hulaki.Ntfy;
using Hulaki.Slack;
using Hulaki.Teams;
using Hulaki.Telegram;
using Hulaki.Webhook;
using Hulaki.WebPush;

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
using var vapid = ECDsa.Create(ECCurve.NamedCurves.nistP256);
var vapidKey = vapid.ExportParameters(includePrivateParameters: true);
using var webPush = new WebPushChannel("webpush", http, new WebPushChannelOptions
{
    VapidPublicKey = Base64Url.EncodeToString([4, .. vapidKey.Q.X!, .. vapidKey.Q.Y!]),
    VapidPrivateKey = Base64Url.EncodeToString(vapidKey.D),
    VapidSubject = "mailto:ops@example.org",
});
using var slack = new SlackChannel("slack", http, new SlackChannelOptions
{
    WebhookUrl = new Uri("https://hooks.slack.com/services/T00000000/B00000000/TESTsecret000000000000000"),
});
using var teams = new TeamsChannel("teams", http, new TeamsChannelOptions
{
    WorkflowUrl = new Uri("https://prod-00.westeurope.logic.azure.com/workflows/0000/triggers/manual/paths/invoke?sig=TESTsig000"),
});
using var bluesky = new BlueskyChannel("bluesky", http, new BlueskyChannelOptions
{
    Identifier = "alerts.example.org",
    AppPassword = "TEST-aaaa-bbbb-cccc",
});
var client = new HulakiClient([telegram, discord, ntfy, webhook, webPush, slack, teams, bluesky]);

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
    new Target("slack", Recipient.Self),
    new Target("teams", Recipient.Self),
    new Target("bluesky", Recipient.Self),
    // The receiver keys of the RFC 8291 section 5 example.
    new Target("webpush", new Recipient("https://push.example.net/push/JzLQ3raZJfFBR0aqvOMsLrt54w4rJUsV", new Dictionary<string, string>
    {
        ["p256dh"] = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
        ["auth"] = "BTBZMqHH6r4Tts7J_aSIgg",
    })),
]);

foreach (var (target, outcome) in result.Outcomes.Where(o => !o.Outcome.Succeeded))
{
    Console.Error.WriteLine($"{target.Channel}: {outcome}");
}

// SMTP has no stub: connect to a closed loopback port instead. That runs MailKit's connect path and
// MimeKit's message building under AOT, and the refusal must come back as a retryable failure.
using var email = new EmailChannel("email", new EmailChannelOptions
{
    Host = "127.0.0.1",
    Port = 9,
    From = "alerts@example.org",
    Retry = SendRetryPolicy.None,
});
var refused = await email.SendAsync(message, new Recipient("reader@example.org"));
if (refused.Error is not { Code: HulakiErrorCode.UpstreamFailure, Retry: RetryDisposition.AfterDelay })
{
    Console.Error.WriteLine($"email: expected a refused connection, got {refused}");
    Console.WriteLine("Failed");
    return 1;
}

// CI compares the whole output with "Delivered".
Console.WriteLine(result.Status == SendStatus.Complete ? "Delivered" : "Failed");
return result.Status == SendStatus.Complete ? 0 : 1;

internal sealed class PlatformStub : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = (request.RequestUri!.Host, request.RequestUri.AbsolutePath) switch
        {
            ("bsky.social", "/xrpc/com.atproto.server.createSession") => """{"did":"did:plc:testaaaaaaaaaaaaaaaaaaaa","accessJwt":"TEST.access.jwt","refreshJwt":"TEST.refresh.jwt"}""",
            ("bsky.social", _) => """{"uri":"at://did:plc:testaaaaaaaaaaaaaaaaaaaa/app.bsky.feed.post/p1","cid":"bafyreitestp1"}""",
            (var host, _) => host switch
            {
            "api.telegram.org" => """{"ok":true,"result":{"message_id":42}}""",
            "discord.com" => """{"id":"1100000000000000001"}""",
            "ntfy.sh" => """{"id":"sPs71M8A2T","event":"message"}""",
            "hooks.example.org" or "push.example.net" or "prod-00.westeurope.logic.azure.com" => "{}",
            "hooks.slack.com" => "ok",
                _ => throw new InvalidOperationException("No stub for this host."),
            },
        };
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        });
    }
}
