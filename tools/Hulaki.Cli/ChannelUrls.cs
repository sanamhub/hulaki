using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using Hulaki.Discord;
using Hulaki.Email;
using Hulaki.Ntfy;
using Hulaki.Telegram;
using Hulaki.Webhook;
using Hulaki.WebPush;

namespace Hulaki.Cli;

/// <summary>One provider as the CLI knows it: its URL schemes, the URL shape and its manifest.</summary>
internal sealed record CliProvider(
    IReadOnlyList<string> Schemes,
    string Format,
    CapabilityManifest Manifest,
    Func<string, Uri, HttpClient, (IChannel? Channel, string? Error)> Create);

/// <summary>
/// The providers the CLI can build from a channel URL. Each provider parses its own URL with an
/// internal parser; this table only routes by scheme. Channel URLs exist for the CLI alone
/// (ADR-0013): a URL holds its secret, so hosts use typed options instead.
/// </summary>
internal static class ChannelUrls
{
    public static IReadOnlyList<CliProvider> Providers { get; } =
    [
        new([DiscordChannelUrl.Scheme], DiscordChannelUrl.Format, DiscordChannel.Manifest,
            (name, url, http) => (DiscordChannelUrl.TryCreate(name, url, http, out var error), error)),
        new([EmailChannelUrl.Scheme], EmailChannelUrl.Format, EmailChannel.Manifest,
            (name, url, _) => (EmailChannelUrl.TryCreate(name, url, out var error), error)),
        new([NtfyChannelUrl.Scheme], NtfyChannelUrl.Format, NtfyChannel.Manifest,
            (name, url, http) => (NtfyChannelUrl.TryCreate(name, url, http, out var error), error)),
        new([TelegramChannelUrl.Scheme], TelegramChannelUrl.Format, TelegramChannel.Manifest,
            (name, url, http) => (TelegramChannelUrl.TryCreate(name, url, http, out var error), error)),
        new([WebhookChannelUrl.Scheme, WebhookChannelUrl.LoopbackScheme], WebhookChannelUrl.Format, WebhookChannel.Manifest,
            (name, url, http) => (WebhookChannelUrl.TryCreate(name, url, http, out var error), error)),
        new([WebPushChannelUrl.Scheme], WebPushChannelUrl.Format, WebPushChannel.Manifest,
            (name, url, http) => (WebPushChannelUrl.TryCreate(name, url, http, out var error), error)),
    ];

    /// <summary>Every manifest, ordered by platform id.</summary>
    public static IReadOnlyList<CapabilityManifest> Manifests { get; } = [.. Providers.Select(p => p.Manifest).OrderBy(m => m.Platform, StringComparer.Ordinal)];

    /// <summary>
    /// Builds a channel from <paramref name="text"/>. <paramref name="redacted"/> is the URL with
    /// only its scheme and host, the one form of it that may be printed.
    /// </summary>
    public static IChannel? TryCreate(string text, HttpClient http, out string redacted, out string? error)
    {
        if (!Uri.TryCreate(text, UriKind.Absolute, out var url))
        {
            redacted = "(not a URL)";
            error = "The channel URL is not an absolute URL.";
            return null;
        }

        redacted = $"{url.Scheme}://{url.Host}";
        var provider = Providers.FirstOrDefault(p => p.Schemes.Contains(url.Scheme, StringComparer.OrdinalIgnoreCase));
        if (provider is null)
        {
            error = $"No provider handles the scheme '{url.Scheme}'. Known: {string.Join(", ", Providers.SelectMany(p => p.Schemes))}.";
            return null;
        }

        var (channel, problem) = provider.Create("cli", url, http);
        error = problem;
        return channel;
    }
}
