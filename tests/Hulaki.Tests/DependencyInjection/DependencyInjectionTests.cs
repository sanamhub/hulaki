using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Hulaki.Channels;
using Hulaki.Idempotency;
using Hulaki.Telegram;
using Hulaki.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hulaki.Extensions.DependencyInjection.Tests;

public sealed class DependencyInjectionTests
{
    private const string Token = "123456:TEST-token_0000000000000000000";

    [Fact]
    public void Telegram_options_bind_from_configuration()
    {
        var configuration = Configuration(new()
        {
            ["Hulaki:Channels:alerts:BotToken"] = Token,
            ["Hulaki:Channels:alerts:BaseAddress"] = "http://localhost:8081/",
            ["Hulaki:Channels:alerts:DisableLinkPreview"] = "false",
            ["Hulaki:Channels:alerts:Retry:ResendUnknown"] = "true",
            ["Hulaki:Channels:alerts:Retry:MaxAttempts"] = "5",
        });
        var services = new ServiceCollection();
        services.AddHulaki().AddTelegram("alerts", configuration.GetSection("Hulaki:Channels:alerts"));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptionsMonitor<TelegramChannelOptions>>().Get("alerts");

        Assert.Equal(Token, options.BotToken);
        Assert.Equal(new Uri("http://localhost:8081/"), options.BaseAddress);
        Assert.False(options.DisableLinkPreview);
        Assert.True(options.Retry.ResendUnknown);
        Assert.Equal(5, options.Retry.MaxAttempts);
        Assert.Equal(SendRetryPolicy.Default.BaseDelay, options.Retry.BaseDelay);
        Assert.False(SendRetryPolicy.Default.ResendUnknown);
        provider.GetRequiredService<IStartupValidator>().Validate();
    }

    [Fact]
    public void A_channel_switched_off_in_configuration_is_not_registered_or_checked()
    {
        var configuration = Configuration(new()
        {
            ["Hulaki:Channels:alerts:Enabled"] = "false",
            ["Hulaki:Channels:ops:BaseAddress"] = "https://ntfy.example.org/",
            ["Hulaki:Channels:ops:Enabled"] = "true",
        });
        var services = new ServiceCollection();
        services.AddHulaki()
            .AddTelegram("alerts", configuration.GetSection("Hulaki:Channels:alerts"))
            .AddNtfy("ops", configuration.GetSection("Hulaki:Channels:ops"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate(); // the empty Telegram token is not checked
        Assert.Null(provider.GetKeyedService<IChannel>("alerts"));
        Assert.NotNull(provider.GetKeyedService<IChannel>("ops"));
    }

    [Fact]
    public void Enabled_that_is_not_a_flag_is_refused_at_registration()
    {
        var configuration = Configuration(new() { ["Hulaki:Channels:alerts:Enabled"] = "off" });

        var error = Assert.Throws<FormatException>(() => new ServiceCollection().AddHulaki().AddTelegram("alerts", configuration.GetSection("Hulaki:Channels:alerts")));

        Assert.Contains("'off'", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_on_start_fails_on_an_empty_token()
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddTelegram("alerts", Configuration(new()).GetSection("Hulaki:Channels:alerts"));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("BotToken is empty", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("http://api.example.test/")]
    [InlineData("ftp://localhost/")]
    public void Validate_on_start_fails_on_a_plain_http_remote_base_address(string address)
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddTelegram("alerts", o =>
        {
            o.BotToken = Token;
            o.BaseAddress = new Uri(address);
        });
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains("BaseAddress", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_telegram_channels_resolve_separately()
    {
        var services = new ServiceCollection();
        services.AddHulaki()
            .AddTelegram("alerts", o => o.BotToken = Token)
            .AddTelegram("ops", o => o.BotToken = "654321:TEST-token_1111111111111111111");
        using var provider = services.BuildServiceProvider();

        var alerts = provider.GetRequiredKeyedService<IChannel>("alerts");
        var ops = provider.GetRequiredKeyedService<IChannel>("ops");
        var client = provider.GetRequiredService<HulakiClient>();

        Assert.NotSame(alerts, ops);
        Assert.Equal("alerts", alerts.Name);
        Assert.Equal("ops", ops.Name);
        Assert.Same(alerts, client.Channels["alerts"]);
        Assert.Same(ops, client.Channels["ops"]);
        Assert.Equal(2, provider.GetServices<IChannel>().Count());
    }

    [Fact]
    public void A_registered_idempotency_store_is_kept()
    {
        var store = new InMemoryIdempotencyStore();
        var services = new ServiceCollection();
        services.AddSingleton<IIdempotencyStore>(store);
        services.AddHulaki();
        using var provider = services.BuildServiceProvider();

        Assert.Same(store, provider.GetRequiredService<IIdempotencyStore>());
    }

    [Fact]
    public async Task No_log_line_contains_the_token_after_a_send_through_the_named_client()
    {
        var logs = new CapturingLoggerProvider();
        using var handler = new ScriptedHttpHandler();
        handler.Respond(HttpStatusCode.OK, """{"ok":true,"result":{"message_id":7}}""");
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Trace));
        services.AddHulaki().AddTelegram("alerts", o => o.BotToken = Token);
        services.AddHttpClient("hulaki.alerts").ConfigurePrimaryHttpMessageHandler(() => handler);
        using var provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<HulakiClient>()
            .SendAsync(new Message("hi"), [new Target("alerts", new Recipient("-1001234567890"))], TestContext.Current.CancellationToken);

        Assert.Equal(SendStatus.Complete, result.Status);
        Assert.Contains(Token, Assert.Single(handler.Requests).Request.RequestUri!.AbsoluteUri, StringComparison.Ordinal);
        Assert.DoesNotContain(logs.Lines, line => line.Contains(Token, StringComparison.Ordinal) || line.Contains("123456", StringComparison.Ordinal));
    }

    private static IConfiguration Configuration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    /// <summary>Keeps every formatted log line, its structured values and any exception text.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _lines = new();

        public IReadOnlyList<string> Lines => [.. _lines];

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _lines);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<string> lines) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                lines.Enqueue($"{category} scope: {state}");
                return null;
            }

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                var values = state is IEnumerable<KeyValuePair<string, object?>> pairs ? string.Join(";", pairs.Select(p => $"{p.Key}={p.Value}")) : string.Empty;
                lines.Enqueue($"{category} {logLevel} {eventId.Id}: {formatter(state, exception)} [{values}] {exception}");
            }
        }
    }
}
