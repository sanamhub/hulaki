using System;
using System.Collections.Generic;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Bluesky;

/// <summary>The configuration-bindable part of <see cref="BlueskyChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class BlueskyChannelSettings : ChannelSettings
{
    public string? Identifier { get; set; }

    public string? AppPassword { get; set; }

    public Uri? ServiceUrl { get; set; }

    public List<string>? Languages { get; set; }

    public bool? ThreadLongPosts { get; set; }

    public static void Apply(IConfiguration section, BlueskyChannelOptions options)
    {
        var settings = section.Get<BlueskyChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.Identifier = settings.Identifier ?? options.Identifier;
        options.AppPassword = settings.AppPassword ?? options.AppPassword;
        options.ServiceUrl = settings.ServiceUrl ?? options.ServiceUrl;
        options.ThreadLongPosts = settings.ThreadLongPosts ?? options.ThreadLongPosts;
        foreach (var language in settings.Languages ?? [])
        {
            options.Languages.Add(language);
        }

        settings.ApplyShared(options);
    }
}
