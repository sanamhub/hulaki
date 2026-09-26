using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Ntfy;

/// <summary>The configuration-bindable part of <see cref="NtfyChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class NtfyChannelSettings : ChannelSettings
{
    public Uri? BaseAddress { get; set; }

    public string? AccessToken { get; set; }

    public static void Apply(IConfiguration section, NtfyChannelOptions options)
    {
        var settings = section.Get<NtfyChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.BaseAddress = settings.BaseAddress ?? options.BaseAddress;
        options.AccessToken = settings.AccessToken ?? options.AccessToken;
        settings.ApplyShared(options);
    }
}
