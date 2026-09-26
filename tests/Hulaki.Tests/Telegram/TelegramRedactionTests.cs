using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hulaki.Testing;
using Hulaki.Tests;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Telegram.Tests;

/// <summary>
/// AC-3.5: after a success, a 403 and a 429, no log line, span tag, metric tag or error message
/// holds the token, the chat id or the message text.
/// </summary>
public sealed class TelegramRedactionTests
{
    private const string Token = "123456:TEST-token_0000000000000000000";
    private const string ChatId = "-1009876543210";
    private const string Text = "Rain warning canary 5b1c for the ridge road";

    public static TheoryData<string> Scenarios() => ["success", "blocked", "rate limited"];

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task Nothing_captured_contains_the_token_chat_or_text(string scenario)
    {
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory();
        var time = new FakeTimeProvider();
        using var handler = new ScriptedHttpHandler(time);
        var echo = $"{Text} {ChatId} {Token}";
        switch (scenario)
        {
            case "success":
                handler.Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":77}}""");
                break;
            case "blocked":
                handler.Respond(HttpStatusCode.Forbidden, $$"""{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user {{echo}}"}""");
                break;
            default:
                handler.Respond(HttpStatusCode.TooManyRequests, $$$"""{"ok":false,"error_code":429,"description":"Too Many Requests: retry after 3 {{{echo}}}","parameters":{"retry_after":3}}""")
                    .Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":78}}""");
                break;
        }

        var channelName = "tg-redaction-" + Guid.NewGuid().ToString("N");
        using var channel = new TelegramChannel(channelName, new HttpClient(handler), new TelegramChannelOptions
        {
            BotToken = Token,
            TimeProvider = time,
            LoggerFactory = logs,
        });

        var outcome = await Time.RunAsync(time, () => channel.SendAsync(new Message(Text) { Title = "Route alert" }, new Recipient(ChatId), TestContext.Current.CancellationToken));

        Assert.NotEmpty(logs.Logs);
        var spans = telemetry.Spans.Where(s => (s.GetTagItem("hulaki.channel") as string) == channelName).ToArray();
        Assert.Single(spans);
        IEnumerable<string> captured =
        [
            .. logs.Logs.SelectMany(l => l.AllText()),
            .. spans.SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}")),
            .. telemetry.Measurements.SelectMany(m => m.Tags.Select(t => $"{t.Key}={t.Value}")),
            outcome.ToString(),
            outcome.Error?.Message ?? string.Empty,
        ];
        foreach (var secret in new[] { Token, "TEST-token", ChatId, Text, "canary 5b1c" })
        {
            Assert.DoesNotContain(captured, text => text.Contains(secret, StringComparison.OrdinalIgnoreCase));
        }
    }
}
