using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Mastodon;

/// <summary>The configuration-bindable part of <see cref="MastodonChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class MastodonChannelSettings : ChannelSettings
{
    public Uri? InstanceUrl { get; set; }

    public string? AccessToken { get; set; }

    public string? Visibility { get; set; }

    public string? Language { get; set; }

    public int? MaxCharacters { get; set; }

    public static void Apply(IConfiguration section, MastodonChannelOptions options)
    {
        var settings = section.Get<MastodonChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.InstanceUrl = settings.InstanceUrl ?? options.InstanceUrl;
        options.AccessToken = settings.AccessToken ?? options.AccessToken;
        options.Visibility = settings.Visibility ?? options.Visibility;
        options.Language = settings.Language ?? options.Language;
        options.MaxCharacters = settings.MaxCharacters ?? options.MaxCharacters;
        settings.ApplyShared(options);
    }
}
