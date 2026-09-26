using System;
using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Teams;

/// <summary>The configuration-bindable part of <see cref="TeamsChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class TeamsChannelSettings : ChannelSettings
{
    public Uri? WorkflowUrl { get; set; }

    public static void Apply(IConfiguration section, TeamsChannelOptions options)
    {
        var settings = section.Get<TeamsChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.WorkflowUrl = settings.WorkflowUrl ?? options.WorkflowUrl;
        settings.ApplyShared(options);
    }
}
