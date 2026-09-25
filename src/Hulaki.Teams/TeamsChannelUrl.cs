using System;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Teams;

/// <summary>
/// <c>teams://&lt;host&gt;/&lt;path&gt;?&lt;query&gt;</c>: the Workflows URL with <c>teams</c> as its
/// scheme. The path and the <c>sig</c> query parameter are never printed. For the CLI only
/// (ADR-0013).
/// </summary>
internal static class TeamsChannelUrl
{
    public const string Scheme = "teams";

    public const string Format = "teams://<workflow host>/<path>?<query>";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var target = new UriBuilder(url) { Scheme = Uri.UriSchemeHttps };
        if (url.IsDefaultPort)
        {
            target.Port = -1;
        }

        var options = new TeamsChannelOptions { WorkflowUrl = target.Uri };
        return ChannelUrlParts.Create(name, options, new TeamsChannelOptionsValidator(), () => new TeamsChannel(name, http, options), out error);
    }
}
