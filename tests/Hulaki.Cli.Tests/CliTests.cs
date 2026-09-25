using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Hulaki.Testing;
using Xunit;

namespace Hulaki.Cli.Tests;

public sealed class CliTests
{
    private const string Token = "123456:TEST-token_0000000000000000000";
    private const string TelegramUrl = "telegram://" + Token + "@telegram";
    private const string VapidPublic = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string VapidPrivate = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";

    private static async Task<(int Exit, string Output, string Error)> RunAsync(ScriptedHttpHandler? handler, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exit = await Cli.RunAsync(args, output, error, handler, TestContext.Current.CancellationToken);
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task Capabilities_lists_every_provider()
    {
        var (exit, output, _) = await RunAsync(null, "capabilities");

        Assert.Equal(Cli.Success, exit);
        foreach (var platform in new[] { "bluesky", "discord", "email", "mastodon", "ntfy", "slack", "teams", "telegram", "webhook", "webpush" })
        {
            Assert.Contains(platform + "\n  text limit:", output, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task The_capabilities_doc_matches_the_command()
    {
        var (_, output, _) = await RunAsync(null, "capabilities", "--markdown");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "Hulaki.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        var committed = await File.ReadAllTextAsync(Path.Combine(root.FullName, "docs", "capabilities.md"), TestContext.Current.CancellationToken);

        // Regenerate with: dotnet run --project tools/Hulaki.Cli -c Release -- capabilities --markdown > docs/capabilities.md
        Assert.Equal(committed.ReplaceLineEndings("\n"), output);
    }

    public static TheoryData<string, string> ValidUrls() => new()
    {
        { TelegramUrl, "telegram" },
        { "discord://123456789012345678:TEST-webhook_token_0000000000@discord", "discord" },
        { "ntfy://ntfy.sh", "ntfy" },
        { "mastodon://TEST-mastodon-token@social.example.org?visibility=unlisted&max=5000", "mastodon" },
        { "bluesky://alerts.example.org:TEST-aaaa-bbbb@bsky.social?lang=ne,en&thread=true", "bluesky" },
        { "slack://hooks.slack.com/services/T00000000/B00000000/TESTsecret0000", "slack" },
        { "teams://prod-00.westeurope.logic.azure.com/workflows/0000/triggers/manual/paths/invoke?api-version=2016-06-01&sig=TESTsig000", "teams" },
        { "ntfy://tk_TESTtoken0000@localhost:8090", "ntfy" },
        { "webhook+https://TEST-secret@hooks.example.org/in", "webhook" },
        { "webhook+http://localhost:5000/in", "webhook" },
        { "smtp://alerts%40example.org:TEST-password@smtp.example.org:465?from=alerts@example.org", "email" },
        { $"webpush://{VapidPublic}:{VapidPrivate}@vapid?subject=mailto:ops@example.org&ttl=600", "webpush" },
    };

    [Theory]
    [MemberData(nameof(ValidUrls))]
    public async Task Doctor_accepts_each_provider_url_and_prints_no_secret(string channel, string platform)
    {
        var (exit, output, error) = await RunAsync(null, "doctor", "--channel-url", channel);

        Assert.Equal(string.Empty, error);
        Assert.Equal(Cli.Success, exit);
        Assert.EndsWith($": OK, platform {platform}\n", output, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST", output, StringComparison.Ordinal);
        Assert.DoesNotContain(VapidPrivate, output, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> InvalidUrls() => new()
    {
        { "telegram://@telegram", "BotToken is empty" },
        { "discord://123:@discord", "WebhookUrl is empty" },
        { $"webpush://{VapidPublic}:{VapidPrivate}@vapid?subject=mailto:ops@example.org&ttl=TEST", "ttl parameter" },
        { "webhook+http://TEST-secret@hooks.example.org/in", "Url must be" },
        { "smtp://smtp.example.org", "From is not" },
        { "mastodon://TEST-token@social.example.org?visibility=followers", "Visibility must be" },
        { "bluesky://alerts.example.org@bsky.social", "AppPassword is empty" },
        { $"webpush://{VapidPublic}:TEST-bad-key@vapid?subject=mailto:ops@example.org", "VapidPrivateKey" },
        { "teams://outlook.office.com/webhook/TESTsecret", "retired" },
        { "slack://example.org/services/TESTsecret", "hooks.slack.com" },
        { "matrix://TEST-secret@hooks", "No provider handles the scheme 'matrix'" },
        { "not a url TEST-secret", "not an absolute URL" },
    };

    [Theory]
    [MemberData(nameof(InvalidUrls))]
    public async Task Doctor_rejects_bad_urls_without_echoing_them(string channel, string expected)
    {
        var (exit, output, error) = await RunAsync(null, "doctor", "--channel-url", channel);

        Assert.Equal(Cli.BadInput, exit);
        Assert.Contains(expected, error + output, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST", error + output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Send_delivers_and_prints_neither_token_recipient_nor_text()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":77}}""");

        var (exit, output, error) = await RunAsync(handler, "send", "--channel-url", TelegramUrl, "--to", "-1009876543210", "--text", "canary 51c0 text", "--title", "Route alert");

        Assert.Equal(Cli.Success, exit);
        Assert.Equal(string.Empty, error);
        Assert.Equal("telegram://telegram: Delivered id 77 after 1 attempt(s)\n", output);
        var request = Assert.Single(handler.Requests);
        Assert.Contains(Token, request.Request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Contains("canary 51c0 text", request.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_send_exits_1_with_the_code()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.Forbidden, """{"ok":false,"error_code":403,"description":"Forbidden: bot was blocked by the user"}""");

        var (exit, output, _) = await RunAsync(handler, "send", "--channel-url", TelegramUrl, "--to", "-1009876543210", "--text", "x");

        Assert.Equal(Cli.DeliveryFailed, exit);
        Assert.Contains("Failed RecipientBlocked after 1 attempt(s)", output, StringComparison.Ordinal);
        Assert.DoesNotContain("-1009876543210", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_to_the_recipient_is_self()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.OK, """{"id":"1100000000000000001"}""");

        var (exit, output, _) = await RunAsync(handler, "send", "--channel-url", "discord://123456789012345678:TEST-webhook_token_0000000000@discord", "--text", "x");

        Assert.Equal(Cli.Success, exit);
        Assert.Equal("discord://discord: Delivered id 1100000000000000001 after 1 attempt(s)\n", output);
        Assert.Equal("https://discord.com/api/webhooks/123456789012345678/TEST-webhook_token_0000000000?wait=true", Assert.Single(handler.Requests).Request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Ntfy_on_loopback_uses_http_and_the_token()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.OK, """{"id":"sPs71M8A2T"}""");

        await RunAsync(handler, "send", "--channel-url", "ntfy://tk_TESTtoken0000@localhost:8090", "--to", "hulaki-topic", "--text", "x");

        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal("http://localhost:8090/", request.RequestUri!.AbsoluteUri);
        Assert.Equal("tk_TESTtoken0000", request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task A_webhook_url_loses_its_prefix_and_its_secret_signs()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.NoContent, string.Empty);

        await RunAsync(handler, "send", "--channel-url", "webhook+https://TEST-secret@hooks.example.org:8443/in?tenant=1", "--text", "x");

        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal("https://hooks.example.org:8443/in?tenant=1", request.RequestUri!.AbsoluteUri);
        Assert.StartsWith("sha256=", request.Headers.GetValues("X-Hulaki-Signature").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Web_push_takes_the_subscription_keys_as_properties()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(_ => new HttpResponseMessage(HttpStatusCode.Created));

        var (exit, output, _) = await RunAsync(
            handler,
            "send",
            "--channel-url", $"webpush://{VapidPublic}:{VapidPrivate}@vapid?subject=mailto:ops@example.org&ttl=600",
            "--to", "https://push.example.net/push/abc",
            "--property", "p256dh=BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4",
            "--property", "auth=BTBZMqHH6r4Tts7J_aSIgg",
            "--text", "x");

        Assert.Equal(Cli.Success, exit);
        Assert.StartsWith("webpush://vapid: Accepted", output, StringComparison.Ordinal);
        Assert.Equal("600", Assert.Single(handler.Requests).Request.Headers.GetValues("TTL").Single());
    }

    [Fact]
    public async Task Missing_properties_are_not_submitted_and_exit_2()
    {
        var (exit, output, _) = await RunAsync(
            new ScriptedHttpHandler(),
            "send",
            "--channel-url", $"webpush://{VapidPublic}:{VapidPrivate}@vapid?subject=mailto:ops@example.org",
            "--to", "https://push.example.net/push/abc",
            "--text", "x");

        Assert.Equal(Cli.BadInput, exit);
        Assert.Contains("NotSubmitted: invalid-p256dh", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_malformed_property_exits_2()
    {
        var (exit, _, error) = await RunAsync(new ScriptedHttpHandler(), "send", "--channel-url", TelegramUrl, "--to", "1", "--property", "novalue", "--text", "x");

        Assert.Equal(Cli.BadInput, exit);
        Assert.Contains("key=value", error, StringComparison.Ordinal);
    }
}
