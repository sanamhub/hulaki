using Hulaki.Channels;

namespace Hulaki.Email;

/// <summary>Options for <see cref="EmailChannel"/>.</summary>
public sealed class EmailChannelOptions : ChannelOptions
{
    /// <summary>The SMTP submission server, for example <c>smtp.example.org</c>.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// The server port. Defaults to 587 (submission with STARTTLS). Port 465 connects with TLS from
    /// the start. STARTTLS is required on every other port, except on a loopback host, where it is
    /// used when the server offers it (a local test server).
    /// </summary>
    public int Port { get; set; } = 587;

    /// <summary>User name for SMTP authentication. Null or empty skips authentication.</summary>
    public string? Username { get; set; }

    /// <summary>Password or app password for SMTP authentication. A secret.</summary>
    public string? Password { get; set; }

    /// <summary>The sender, for example <c>Route alerts &lt;alerts@example.org&gt;</c>.</summary>
    public string From { get; set; } = string.Empty;
}
