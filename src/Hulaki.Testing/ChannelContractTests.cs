using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Hulaki.Testing;

/// <summary>
/// The behaviour every channel must have, as xUnit v3 facts (ADR-0014). Derive a test class per
/// provider, implement the three members, and the facts run in your test project.
/// </summary>
/// <typeparam name="TChannel">The channel under test.</typeparam>
public abstract class ChannelContractTests<TChannel>
    where TChannel : IChannel
{
    // Distinctive so a leak is unmistakable in the error message.
    private const string CanaryText = "contract canary 7f3a91 text";

    /// <summary>A recipient the channel accepts. Its address must not appear in any error message.</summary>
    protected abstract Recipient ValidRecipient { get; }

    /// <summary>Creates the channel under test over <paramref name="handler"/>.</summary>
    /// <param name="handler">Answers every request. Wrap it in an <see cref="HttpClient"/>.</param>
    /// <param name="time">Clock for backoff and pauses. Pass it to the channel options.</param>
    /// <returns>A channel with valid configuration and no retry policy changes.</returns>
    protected abstract TChannel Create(HttpMessageHandler handler, TimeProvider time);

    /// <summary>A response the platform sends for a successful send.</summary>
    /// <returns>A new response each call.</returns>
    protected abstract HttpResponseMessage Success();

    /// <summary><see cref="IChannel.Prepare"/> is pure: it makes no request (AC-3.4).</summary>
    [Fact]
    public void Prepare_does_no_io()
    {
        using var handler = ScriptedHttpHandler.ThrowOnAnyRequest();
        var channel = Create(handler, new InstantTimeProvider());
        try
        {
            channel.Prepare(new Message(CanaryText), ValidRecipient);
        }
        finally
        {
            (channel as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// A refused connection sent nothing, so the base class retries it. A provider that catches
    /// <see cref="HttpRequestException"/> itself fails this.
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Connection_refused_is_retried()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Throw(new HttpRequestException(HttpRequestError.ConnectionError, "Connection refused.")).Respond(_ => Success());

        var outcome = await SendAsync(handler, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Check(outcome.Succeeded, $"expected success after one refused connection, got {outcome}");
        Check(handler.Requests.Count == 2, $"expected 2 requests, got {handler.Requests.Count}");
    }

    /// <summary>
    /// A platform error that quotes the message and the recipient back must not reach
    /// <see cref="HulakiError.Message"/> (ADR-0007, AC-3.5).
    /// </summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Errors_do_not_echo_content()
    {
        var address = ValidRecipient.Address;
        var echo = $"{CanaryText} {address}";
        var body = $$"""{"ok":false,"error_code":400,"description":"{{echo}}","message":"{{echo}}","error":"{{echo}}","detail":"{{echo}}"}""";
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.BadRequest, body);

        var outcome = await SendAsync(handler, TestContext.Current.CancellationToken).ConfigureAwait(false);

        Check(outcome.Error is not null, $"expected an error for a 400, got {outcome}");
        var message = outcome.Error!.Message;
        Check(!message.Contains(CanaryText, StringComparison.OrdinalIgnoreCase), "HulakiError.Message contains the message text");
        Check(address.Length == 0 || !message.Contains(address, StringComparison.OrdinalIgnoreCase), "HulakiError.Message contains the recipient address");
    }

    /// <summary>A caller's cancellation throws instead of becoming an outcome (ADR-0005).</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task Caller_cancellation_throws()
    {
        using var handler = new ScriptedHttpHandler();
        handler.Respond(_ => Success());
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync().ConfigureAwait(false);

        DeliveryOutcome? outcome = null;
        try
        {
            outcome = await SendAsync(handler, cancelled.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        Check(false, $"expected OperationCanceledException, got the outcome {outcome}");
    }

    private async Task<DeliveryOutcome> SendAsync(ScriptedHttpHandler handler, CancellationToken cancellationToken)
    {
        var channel = Create(handler, new InstantTimeProvider());
        try
        {
            return await channel.SendAsync(new Message(CanaryText), ValidRecipient, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            (channel as IDisposable)?.Dispose();
        }
    }

    // The kit ships without an assertion library so consumers keep their own; a failed check
    // throws, and xUnit reports the message.
    private static void Check(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException("Channel contract violated: " + failure);
        }
    }
}
