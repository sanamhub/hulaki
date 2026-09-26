using System;
using System.Net.Http;
using Hulaki.Providers;

namespace Hulaki.Telegram;

/// <summary>
/// <c>telegram://&lt;bot-token&gt;@telegram</c>, optionally <c>?base=&lt;url&gt;</c> for a local Bot API
/// server. For the CLI only (ADR-0013); hosts use <see cref="TelegramChannelOptions"/>.
/// </summary>
internal static class TelegramChannelUrl
{
    public const string Scheme = "telegram";

    public const string Format = "telegram://<bot-token>@telegram[?base=<bot api url>]";

    public static IChannel? TryCreate(string name, Uri url, HttpClient http, out string? error)
    {
        var options = new TelegramChannelOptions { BotToken = ChannelUrlParts.UserInfo(url) };
        if (ChannelUrlParts.Query(url).TryGetValue("base", out var baseAddress))
        {
            if (!Uri.TryCreate(baseAddress, UriKind.Absolute, out var parsed))
            {
                error = "The base parameter is not an absolute URL.";
                return null;
            }

            options.BaseAddress = parsed;
        }

        return ChannelUrlParts.Create(name, options, new TelegramChannelOptionsValidator(), () => new TelegramChannel(name, http, options), out error);
    }
}
