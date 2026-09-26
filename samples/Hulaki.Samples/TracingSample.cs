using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Hulaki.Samples;

// README: "OpenTelemetry". Telegram puts the bot token in the path, and Discord and Slack webhook
// URLs are secrets in the path. .NET redacts query strings in url.full, not paths, so leave these
// hosts out of HTTP client spans. Hulaki's own spans carry no URL.
internal static class TracingSample
{
    private static readonly string[] SecretInPathHosts = ["api.telegram.org", "discord.com", "hooks.slack.com"];

    public static TracerProvider Build() =>
        Sdk.CreateTracerProviderBuilder()
            .AddSource("Hulaki")
            .AddHttpClientInstrumentation(o => o.FilterHttpRequestMessage =
                request => request.RequestUri is not { } uri || !SecretInPathHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            .Build();
}
