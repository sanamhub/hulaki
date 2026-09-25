using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Telegram;

/// <summary>The configuration-bindable part of <see cref="TelegramChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class TelegramChannelSettings : ChannelSettings
{
    public string? BotToken { get; set; }

    public Uri? BaseAddress { get; set; }

    public bool? DisableLinkPreview { get; set; }

    public static void Apply(IConfiguration section, TelegramChannelOptions options)
    {
        var settings = section.Get<TelegramChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.BotToken = settings.BotToken ?? options.BotToken;
        options.BaseAddress = settings.BaseAddress ?? options.BaseAddress;
        options.DisableLinkPreview = settings.DisableLinkPreview ?? options.DisableLinkPreview;
        settings.ApplyShared(options);
    }
}
