using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Hulaki.Email.Tests;

/// <summary>Records what the channel does with each SMTP session and fails a phase on request.</summary>
internal sealed class FakeSmtpServer
{
    private readonly ConcurrentQueue<Func<Exception?>> _connect = new();
    private readonly ConcurrentQueue<Func<Exception?>> _send = new();

    public List<(string Host, int Port, SecureSocketOptions Security)> Connections { get; } = [];

    public List<(string User, string Password)> Logins { get; } = [];

    public List<MimeMessage> Sent { get; } = [];

    public int Disconnects { get; private set; }

    public Exception? DisconnectFailure { get; set; }

    public FakeSmtpServer FailConnect(Exception exception)
    {
        _connect.Enqueue(() => exception);
        return this;
    }

    public FakeSmtpServer FailSend(Exception exception)
    {
        _send.Enqueue(() => exception);
        return this;
    }

    public ISmtpSession Open() => new Session(this);

    private sealed class Session(FakeSmtpServer server) : ISmtpSession
    {
        public Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            server.Connections.Add((host, port, security));
            return server._connect.TryDequeue(out var fail) && fail() is { } ex ? Task.FromException(ex) : Task.CompletedTask;
        }

        public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken)
        {
            server.Logins.Add((userName, password));
            return Task.CompletedTask;
        }

        public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
        {
            if (server._send.TryDequeue(out var fail) && fail() is { } ex)
            {
                throw ex;
            }

            // The channel disposes its message after the send; keep a copy for the assertions.
            using var copy = new System.IO.MemoryStream();
            await message.WriteToAsync(copy, cancellationToken);
            copy.Position = 0;
            server.Sent.Add(await MimeMessage.LoadAsync(copy, cancellationToken));
        }

        public Task DisconnectAsync(CancellationToken cancellationToken)
        {
            server.Disconnects++;
            return server.DisconnectFailure is { } ex ? Task.FromException(ex) : Task.CompletedTask;
        }

        public void Dispose()
        {
        }
    }
}

/// <summary>
/// Adapts the HTTP-scripted contract kit to SMTP: the connect phase makes one request to the
/// scripted handler. A thrown <see cref="HttpRequestException"/> becomes a refused TCP connection;
/// a non-2xx answer becomes a 554 reply to DATA carrying the response body as the server's text,
/// so the kit's "errors do not echo content" check reaches the SMTP error mapping.
/// </summary>
internal sealed class HttpScriptedSmtpSession(HttpClient http) : ISmtpSession
{
    private HttpResponseMessage? _answer;

    public async Task ConnectAsync(string host, int port, SecureSocketOptions security, CancellationToken cancellationToken)
    {
        try
        {
            _answer = await http.PostAsync(new Uri("https://smtp.test/session"), new StringContent(host), cancellationToken);
        }
        catch (HttpRequestException)
        {
            throw new SocketException((int)SocketError.ConnectionRefused);
        }
    }

    public Task AuthenticateAsync(string userName, string password, CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task SendAsync(MimeMessage message, CancellationToken cancellationToken)
    {
        if (_answer is { IsSuccessStatusCode: false } answer)
        {
            var text = await answer.Content.ReadAsStringAsync(cancellationToken);
            throw new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, text);
        }
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public void Dispose() => _answer?.Dispose();
}
