using System;
using System.Collections.Generic;
using System.CommandLine;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Hulaki.Cli;

/// <summary>
/// The <c>hulaki</c> commands. Nothing printed contains a channel URL's secret, a recipient address
/// or the message text: channels are shown as <c>scheme://host</c> and outcomes by status and code.
/// </summary>
internal static class Cli
{
    public const int Success = 0;
    public const int DeliveryFailed = 1;
    public const int BadInput = 2;

    /// <summary>Runs the command line.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="output">Standard output.</param>
    /// <param name="error">Standard error.</param>
    /// <param name="handler">The HTTP handler for sends; null for a real one. Tests pass a scripted handler.</param>
    /// <param name="cancellationToken">Cancels a send.</param>
    public static Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, HttpMessageHandler? handler, CancellationToken cancellationToken)
    {
        var root = new RootCommand("Hulaki: deliver a message to chat, push and social platforms.");
        root.Subcommands.Add(Capabilities(output));
        root.Subcommands.Add(Send(output, error, handler));
        root.Subcommands.Add(Doctor(output, error));
        return root.Parse(args).InvokeAsync(new InvocationConfiguration { Output = output, Error = error }, cancellationToken);
    }

    private static Command Capabilities(TextWriter output)
    {
        var markdown = new Option<bool>("--markdown") { Description = "Print the Markdown of docs/capabilities.md." };
        var command = new Command("capabilities", "Print what every provider supports, from its capability manifest.") { markdown };
        command.SetAction(result =>
        {
            output.Write(result.GetValue(markdown) ? CapabilityReport.Markdown(ChannelUrls.Manifests) : CapabilityReport.Text(ChannelUrls.Manifests));
            return Success;
        });
        return command;
    }

    private static Command Send(TextWriter output, TextWriter error, HttpMessageHandler? handler)
    {
        var channelUrl = ChannelUrlOption();
        var to = new Option<string>("--to") { Description = "The recipient address. Omit it for channels that post to their own feed or webhook." };
        var text = new Option<string>("--text") { Description = "The message text.", Required = true };
        var title = new Option<string>("--title") { Description = "An optional title." };
        var properties = new Option<string[]>("--property")
        {
            Description = "A recipient property as key=value, repeatable. Web Push needs p256dh and auth.",
            AllowMultipleArgumentsPerToken = false,
        };
        var command = new Command("send", "Send one message through a channel URL and print the outcome.") { channelUrl, to, text, title, properties };
        command.SetAction(async (result, cancellationToken) =>
        {
            using var http = new HttpClient(handler ?? new SocketsHttpHandler(), disposeHandler: handler is null) { Timeout = TimeSpan.FromSeconds(30) };
            var channel = ChannelUrls.TryCreate(result.GetRequiredValue(channelUrl), http, out var redacted, out var problem);
            if (channel is null)
            {
                await error.WriteAsync($"{redacted}: {problem}\n").ConfigureAwait(false);
                return BadInput;
            }

            using var disposable = channel as IDisposable;
            if (!TryProperties(result.GetValue(properties) ?? [], out var recipientProperties))
            {
                await error.WriteAsync("--property takes key=value.\n").ConfigureAwait(false);
                return BadInput;
            }

            var address = result.GetValue(to);
            var recipient = string.IsNullOrEmpty(address) ? Recipient.Self : new Recipient(address, recipientProperties);
            var message = new Message(result.GetRequiredValue(text)) { Title = result.GetValue(title) };
            var outcome = await channel.SendAsync(message, recipient, cancellationToken).ConfigureAwait(false);

            await output.WriteAsync($"{redacted}: {Describe(outcome)}\n").ConfigureAwait(false);
            return outcome.Succeeded ? Success : outcome.Status == DeliveryStatus.NotSubmitted ? BadInput : DeliveryFailed;
        });
        return command;
    }

    private static Command Doctor(TextWriter output, TextWriter error)
    {
        var channelUrl = ChannelUrlOption();
        var command = new Command("doctor", "Check a channel URL and build the channel without sending anything.") { channelUrl };
        command.SetAction(async result =>
        {
            // No request is made: the handler refuses any.
            using var http = new HttpClient(new NoRequests());
            var channel = ChannelUrls.TryCreate(result.GetRequiredValue(channelUrl), http, out var redacted, out var problem);
            if (channel is null)
            {
                await error.WriteAsync($"{redacted}: {problem}\n").ConfigureAwait(false);
                return BadInput;
            }

            (channel as IDisposable)?.Dispose();
            await output.WriteAsync($"{redacted}: OK, platform {channel.Capabilities.Platform}\n").ConfigureAwait(false);
            return Success;
        });
        return command;
    }

    private static Option<string> ChannelUrlOption() => new("--channel-url")
    {
        Description = "The channel as a URL, for example telegram://<bot-token>@telegram. It holds a secret: prefer an environment variable to typing it, so it stays out of shell history.",
        Required = true,
    };

    private static bool TryProperties(string[] pairs, out Dictionary<string, string> properties)
    {
        properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0)
            {
                return false;
            }

            properties[pair[..equals]] = pair[(equals + 1)..];
        }

        return true;
    }

    // Status, error code and Hulaki's own error text: never the platform's words or the address.
    private static string Describe(DeliveryOutcome outcome)
    {
        var id = outcome.PlatformMessageId is null ? string.Empty : $" id {outcome.PlatformMessageId}";
        return outcome switch
        {
            { Error: { } e } => $"{outcome.Status} {e.Code} after {outcome.Attempts} attempt(s): {e.Message}",
            { Status: DeliveryStatus.NotSubmitted } => $"{outcome.Status}: {string.Join("; ", outcome.Issues.Where(i => i.IsError).Select(i => $"{i.Code} ({i.Message})"))}",
            _ => $"{outcome.Status}{id} after {outcome.Attempts} attempt(s)",
        };
    }

    private sealed class NoRequests : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("doctor makes no requests.");
    }
}
