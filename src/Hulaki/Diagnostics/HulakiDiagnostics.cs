using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;

namespace Hulaki.Diagnostics;

/// <summary>
/// The one <see cref="System.Diagnostics.ActivitySource"/> and one <see cref="System.Diagnostics.Metrics.Meter"/>
/// Hulaki reports to, both named <c>Hulaki</c> (ADR-0012). Tags carry names, codes and counts only:
/// never message text, titles, links, recipient addresses, tokens or platform descriptions.
/// </summary>
internal static class HulakiDiagnostics
{
    public const string SourceName = "Hulaki";
    public const string SendActivityName = "hulaki.send";

    public const string ChannelTag = "hulaki.channel";
    public const string PlatformTag = "hulaki.platform";
    public const string StatusTag = "hulaki.status";
    public const string ErrorCodeTag = "hulaki.error.code";
    public const string AttemptsTag = "hulaki.attempts";
    public const string IdempotentReplayTag = "hulaki.idempotent_replay";
    public const string ReasonTag = "hulaki.reason";

    /// <summary>The package version, without the source revision that the SDK appends after <c>+</c>.</summary>
    public static readonly string Version = PackageVersion();

    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    public static readonly Meter Meter = new(SourceName, Version);

    public static readonly Counter<long> Sends = Meter.CreateCounter<long>(
        "hulaki.sends", unit: "{send}", description: "Sends to one target, by final status.");

    public static readonly Histogram<double> SendDuration = Meter.CreateHistogram<double>(
        "hulaki.send.duration", unit: "s", description: "Time for a send to one target, including retries and waits.");

    public static readonly Counter<long> Retries = Meter.CreateCounter<long>(
        "hulaki.retries", unit: "{retry}", description: "Attempts after the first, by the error that caused them.");

    /// <summary>Starts the <c>hulaki.send</c> span, or returns null when nothing listens.</summary>
    public static Activity? StartSend(string channel, string platform)
    {
        var activity = ActivitySource.StartActivity(SendActivityName, ActivityKind.Client);
        activity?.SetTag(ChannelTag, channel);
        activity?.SetTag(PlatformTag, platform);
        return activity;
    }

    /// <summary>Sets the final tags of ADR-0012 on a <c>hulaki.send</c> span.</summary>
    public static void Complete(Activity? activity, DeliveryOutcome outcome, bool isReplay)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(StatusTag, outcome.Status.ToString());
        if (outcome.Error is { } error)
        {
            activity.SetTag(ErrorCodeTag, error.Code.ToString());
        }

        activity.SetTag(AttemptsTag, outcome.Attempts);
        activity.SetTag(IdempotentReplayTag, isReplay);
        if (!outcome.Succeeded)
        {
            // The code is safe to report; the error message is left out on purpose.
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    /// <summary>Records <c>hulaki.sends</c> and <c>hulaki.send.duration</c> for one finished send.</summary>
    public static void RecordSend(string platform, DeliveryOutcome outcome, TimeSpan elapsed)
    {
        var status = new KeyValuePair<string, object?>(StatusTag, outcome.Status.ToString());
        var platformTag = new KeyValuePair<string, object?>(PlatformTag, platform);
        if (outcome.Error is { } error)
        {
            Sends.Add(1, platformTag, status, new KeyValuePair<string, object?>(ErrorCodeTag, error.Code.ToString()));
        }
        else
        {
            Sends.Add(1, platformTag, status);
        }

        SendDuration.Record(elapsed.TotalSeconds, platformTag, status);
    }

    /// <summary>Records one <c>hulaki.retries</c> increment.</summary>
    public static void RecordRetry(string platform, HulakiErrorCode reason) =>
        Retries.Add(1, new KeyValuePair<string, object?>(PlatformTag, platform), new KeyValuePair<string, object?>(ReasonTag, reason.ToString()));

    private static string PackageVersion()
    {
        var version = typeof(HulakiDiagnostics).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? typeof(HulakiDiagnostics).Assembly.GetName().Version?.ToString()
            ?? "0.0.0";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
