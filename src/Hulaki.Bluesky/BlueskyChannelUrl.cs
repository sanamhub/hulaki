using System;
using System.Collections.Generic;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Bluesky;

/// <summary>
/// <c>bluesky://&lt;handle&gt;:&lt;app-password&gt;@&lt;server&gt;[?lang=ne,en&amp;thread=true]</c>, the
/// server <c>bsky.social</c> for most accounts. For the CLI only (ADR-0013).
/// </summary>
internal static class BlueskyChannelUrl
{
    public const string Scheme = "bluesky";

    public const string Format = "bluesky://<handle>:<app-password>@bsky.social[?lang=ne,en&thread=true]";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var (handle, password) = ChannelUrlParts.Credentials(url);
        var query = ChannelUrlParts.Query(url);
        var options = new BlueskyChannelOptions
        {
            Identifier = handle,
            AppPassword = password ?? string.Empty,
            ServiceUrl = new UriBuilder(url.IsLoopback ? Uri.UriSchemeHttp : Uri.UriSchemeHttps, url.Host, url.IsDefaultPort ? -1 : url.Port).Uri,
            ThreadLongPosts = query.TryGetValue("thread", out var thread) && thread.Equals("true", StringComparison.OrdinalIgnoreCase),
        };
        foreach (var language in query.GetValueOrDefault("lang", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            options.Languages.Add(language);
        }

        return ChannelUrlParts.Create(name, options, new BlueskyChannelOptionsValidator(), () => new BlueskyChannel(name, http, options), out error);
    }
}
