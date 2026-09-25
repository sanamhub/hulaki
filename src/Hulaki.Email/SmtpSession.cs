using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Hulaki.Email;

/// <summary>
/// One SMTP connection, split into the phases the error mapping needs to tell apart: a failure
/// while connecting sent nothing, a failure while sending may have delivered the message. Tests
/// replace it; <see cref="MailKitSmtpSession"/> is the real one.
/// </summary>
internal interface ISmtpSession : IDisposable
{
    Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken);

    Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken);

    Task SendAsync(MimeMessage message, CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}

/// <summary>A MailKit <see cref="SmtpClient"/> per send; pooling connections is later work (P4).</summary>
internal sealed class MailKitSmtpSession : ISmtpSession
{
    private readonly SmtpClient _client = new() { Timeout = 30_000 };

    public Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken) =>
        _client.ConnectAsync(host, port, security, cancellationToken);

    public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) =>
        _client.AuthenticateAsync(userName, password, cancellationToken);

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken) =>
        _client.SendAsync(message, cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        _client.DisconnectAsync(quit: true, cancellationToken);

    public void Dispose() => _client.Dispose();
}
