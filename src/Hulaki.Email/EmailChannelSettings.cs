using Hulaki.Providers;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Email;

/// <summary>The configuration-bindable part of <see cref="EmailChannelOptions"/>; see <see cref="ChannelSettings"/>.</summary>
internal sealed class EmailChannelSettings : ChannelSettings
{
    public bool? UseFreeTierMail { get; set; }

    public string? Host { get; set; }

    public int? Port { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? From { get; set; }

    public static void Apply(IConfiguration section, EmailChannelOptions options)
    {
        var settings = section.Get<EmailChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.UseFreeTierMail = settings.UseFreeTierMail ?? options.UseFreeTierMail;
        options.Host = settings.Host ?? options.Host;
        options.Port = settings.Port ?? options.Port;
        options.Username = settings.Username ?? options.Username;
        options.Password = settings.Password ?? options.Password;
        options.From = settings.From ?? options.From;
        settings.ApplyShared(options);
    }
}
