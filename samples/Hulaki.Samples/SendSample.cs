using Hulaki;
using Hulaki.Telegram;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Samples;

// README: "Send a message".
internal static class SendSample
{
    public static async Task RunAsync(IConfiguration configuration)
    {
        using var http = new HttpClient();
        using var telegram = new TelegramChannel("alerts", http, new TelegramChannelOptions
        {
            BotToken = configuration["Hulaki:Channels:alerts:BotToken"]!,
        });
        var client = new HulakiClient([telegram]);

        var message = new Message("**Orange** rain warning for Myagdi on 26 Sep. [DHM](https://dhm.gov.np)")
        {
            Format = TextFormat.Markup,
            Title = "Route alert: Pokhara to Beni",
            Priority = MessagePriority.High,
            IdempotencyKey = "watch-42:2026-09-26:rev-7",
        };

        var result = await client.SendAsync(message, [new Target("alerts", new Recipient("123456789"))]);
        foreach (var (target, outcome) in result.Outcomes)
        {
            switch (outcome.Error?.Code)
            {
                case null: break;                                   // delivered or accepted
                case HulakiErrorCode.RecipientBlocked:
                case HulakiErrorCode.RecipientNotFound: /* disable this subscription */ break;
                case HulakiErrorCode.RateLimited: /* reschedule after outcome.Error.RetryAfter */ break;
                default: /* log outcome.Error.Code; never the recipient */ break;
            }
        }
    }
}
