using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Hulaki.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Tests;

public sealed class DiagnosticsTests
{
    private static readonly Message Hello = new("hello");

    [Fact]
    public async Task Delivered_send_reports_the_span_metrics_and_log_of_adr_0012()
    {
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory();
        var platform = UniquePlatform();
        var channel = new ScriptedChannel(new TestOptions { LoggerFactory = logs }, Manifest(platform), () => DeliveryOutcome.Delivered("1"));

        await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        var span = Assert.Single(telemetry.Spans, s => Tag(s, "hulaki.platform") == platform);
        Assert.Equal("hulaki.send", span.OperationName);
        Assert.Equal(ActivityKind.Client, span.Kind);
        Assert.Equal("Hulaki", span.Source.Name);
        Assert.Equal(PackageVersion(), span.Source.Version);
        Assert.Equal(
            new Dictionary<string, object?>
            {
                ["hulaki.channel"] = "scripted",
                ["hulaki.platform"] = platform,
                ["hulaki.status"] = "Delivered",
                ["hulaki.attempts"] = 1,
                ["hulaki.idempotent_replay"] = false,
            },
            span.TagObjects.ToDictionary(t => t.Key, t => t.Value));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);

        var sends = Assert.Single(Measurements(telemetry, "hulaki.sends", platform));
        Assert.Equal(1, sends.Value);
        Assert.Equal("{send}", sends.Unit);
        Assert.Equal(new Dictionary<string, object?> { ["hulaki.platform"] = platform, ["hulaki.status"] = "Delivered" }, sends.Tags);
        var duration = Assert.Single(Measurements(telemetry, "hulaki.send.duration", platform));
        Assert.Equal("s", duration.Unit);
        Assert.Equal(new Dictionary<string, object?> { ["hulaki.platform"] = platform, ["hulaki.status"] = "Delivered" }, duration.Tags);
        Assert.Empty(Measurements(telemetry, "hulaki.retries", platform));

        var log = Assert.Single(logs.Logs);
        Assert.Equal((1, LogLevel.Debug), (log.EventId, log.Level));
    }

    [Fact]
    public async Task Rate_limited_retry_reports_the_pause_the_retry_and_the_duration()
    {
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory();
        var platform = UniquePlatform();
        var time = new FakeTimeProvider();
        var limited = new HulakiError(HulakiErrorCode.RateLimited, RetryDisposition.AfterDelay, "slow down") { RetryAfter = TimeSpan.FromSeconds(5) };
        var channel = new ScriptedChannel(
            new TestOptions { LoggerFactory = logs, TimeProvider = time },
            Manifest(platform),
            () => DeliveryOutcome.Failed(limited),
            () => DeliveryOutcome.Delivered());

        await Time.RunAsync(time, () => channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken));

        var span = Assert.Single(telemetry.Spans, s => Tag(s, "hulaki.platform") == platform);
        Assert.Equal(2, span.GetTagItem("hulaki.attempts"));
        var retry = Assert.Single(Measurements(telemetry, "hulaki.retries", platform));
        Assert.Equal(new Dictionary<string, object?> { ["hulaki.platform"] = platform, ["hulaki.reason"] = "RateLimited" }, retry.Tags);
        Assert.True(Assert.Single(Measurements(telemetry, "hulaki.send.duration", platform)).Value >= 5);
        Assert.Equal([5, 2, 1], logs.Logs.Select(l => l.EventId));
        Assert.Equal([LogLevel.Information, LogLevel.Information, LogLevel.Debug], logs.Logs.Select(l => l.Level));
    }

    [Fact]
    public async Task Failed_send_reports_the_error_code_and_warns()
    {
        using var telemetry = new TelemetryCapture();
        var logs = new CapturingLoggerFactory();
        var platform = UniquePlatform();
        var channel = new ScriptedChannel(
            new TestOptions { LoggerFactory = logs },
            Manifest(platform),
            () => DeliveryOutcome.Failed(new HulakiError(HulakiErrorCode.RecipientBlocked, RetryDisposition.Never, "blocked")));

        await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        var span = Assert.Single(telemetry.Spans, s => Tag(s, "hulaki.platform") == platform);
        Assert.Equal("Failed", span.GetTagItem("hulaki.status"));
        Assert.Equal("RecipientBlocked", span.GetTagItem("hulaki.error.code"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        var sends = Assert.Single(Measurements(telemetry, "hulaki.sends", platform));
        Assert.Equal("RecipientBlocked", sends.Tags["hulaki.error.code"]);
        var log = Assert.Single(logs.Logs);
        Assert.Equal((3, LogLevel.Warning), (log.EventId, log.Level));
    }

    [Fact]
    public async Task Unknown_outcome_warns()
    {
        var logs = new CapturingLoggerFactory();
        var channel = new ScriptedChannel(
            new TestOptions { LoggerFactory = logs },
            null,
            () => DeliveryOutcome.Unknown(new HulakiError(HulakiErrorCode.AmbiguousOutcome, RetryDisposition.ReconcileFirst, "lost")));

        await channel.SendAsync(Hello, new Recipient("1"), TestContext.Current.CancellationToken);

        var log = Assert.Single(logs.Logs);
        Assert.Equal((4, LogLevel.Warning), (log.EventId, log.Level));
    }

    [Fact]
    public async Task Idempotent_replay_gets_its_own_span()
    {
        using var telemetry = new TelemetryCapture();
        var platform = UniquePlatform();
        var channel = new ScriptedChannel(new TestOptions(), Manifest(platform), () => DeliveryOutcome.Delivered());
        var client = new HulakiClient([channel]);
        var message = Hello with { IdempotencyKey = "replay-span" };
        Target[] targets = [new("scripted", new Recipient("1"))];

        await client.SendAsync(message, targets, TestContext.Current.CancellationToken);
        await client.SendAsync(message, targets, TestContext.Current.CancellationToken);

        var spans = telemetry.Spans.Where(s => Tag(s, "hulaki.platform") == platform).ToArray();
        Assert.Equal(2, spans.Length);
        Assert.Equal([false, true], spans.Select(s => s.GetTagItem("hulaki.idempotent_replay")));
        Assert.Equal(1, channel.Calls);
    }

    [Fact]
    public async Task Client_logs_a_conflict_and_a_provider_exception()
    {
        var logs = new CapturingLoggerFactory();
        var logger = new Logger<HulakiClient>(logs);
        var scripted = new ScriptedChannel(new TestOptions(), null, () => DeliveryOutcome.Delivered());
        var client = new HulakiClient([scripted, new Hulaki.Testing.FakeChannel("broken", respond: (_, _) => throw new InvalidOperationException("provider bug"))], logger: logger);
        var ct = TestContext.Current.CancellationToken;

        await client.SendAsync(Hello with { IdempotencyKey = "k" }, [new Target("scripted", new Recipient("1"))], ct);
        await client.SendAsync(new Message("changed") { IdempotencyKey = "k" }, [new Target("scripted", new Recipient("1"))], ct);
        await client.SendAsync(Hello, [new Target("broken", new Recipient("1"))], ct);

        Assert.Equal([6, 7], logs.Logs.Select(l => l.EventId));
        Assert.Equal([LogLevel.Warning, LogLevel.Error], logs.Logs.Select(l => l.Level));
        Assert.Contains("ChannelCount=1", logs.Logs[0].Values);
        Assert.Contains("ExceptionType=InvalidOperationException", logs.Logs[1].Values);
        Assert.Null(logs.Logs[1].Exception);
        Assert.DoesNotContain(logs.Logs.SelectMany(l => l.AllText()), text => text.Contains("provider bug", StringComparison.Ordinal));
    }

    private static string UniquePlatform() => "diag-" + Guid.NewGuid().ToString("N");

    private static CapabilityManifest Manifest(string platform) =>
        new(platform, new TextLimit(20, TextCounter.Graphemes), [new(Capability.Text, Availability.Available)]);

    private static string? Tag(Activity activity, string key) => activity.GetTagItem(key) as string;

    private static IEnumerable<CapturedMeasurement> Measurements(TelemetryCapture telemetry, string instrument, string platform) =>
        telemetry.Measurements.Where(m => m.Instrument == instrument && Equals(m.Tags.GetValueOrDefault("hulaki.platform"), platform));

    private static string PackageVersion() =>
        typeof(HulakiClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion.Split('+')[0];
}
