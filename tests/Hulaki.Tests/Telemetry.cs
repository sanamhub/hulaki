using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Hulaki.Tests;

/// <summary>An <see cref="ILoggerFactory"/> that keeps every log line with its event id, structured values and exception.</summary>
/// <remarks>Also compiled into Hulaki.Telegram.Tests.</remarks>
internal sealed class CapturingLoggerFactory : ILoggerFactory
{
    private readonly ConcurrentQueue<CapturedLog> _logs = new();

    public IReadOnlyList<CapturedLog> Logs => [.. _logs];

    public ILogger CreateLogger(string categoryName) => new Logger(categoryName, _logs);

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public void Dispose()
    {
    }

    private sealed class Logger(string category, ConcurrentQueue<CapturedLog> logs) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.Select(p => $"{p.Key}={p.Value}").ToArray()
                : [];
            logs.Enqueue(new CapturedLog(category, logLevel, eventId.Id, formatter(state, exception), values, exception?.ToString()));
        }
    }
}

internal sealed record CapturedLog(string Category, LogLevel Level, int EventId, string Message, IReadOnlyList<string> Values, string? Exception)
{
    public IEnumerable<string> AllText() => [Category, Message, .. Values, Exception ?? string.Empty];
}

/// <summary>Collects every Hulaki span and measurement while it lives. Filter by tag: other tests run in parallel.</summary>
/// <remarks>Also compiled into Hulaki.Telegram.Tests.</remarks>
internal sealed class TelemetryCapture : IDisposable
{
    private readonly ActivityListener _activities;
    private readonly MeterListener _meters = new();
    private readonly ConcurrentQueue<Activity> _spans = new();
    private readonly ConcurrentQueue<CapturedMeasurement> _measurements = new();

    public TelemetryCapture()
    {
        _activities = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Hulaki",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _spans.Enqueue,
        };
        ActivitySource.AddActivityListener(_activities);

        _meters.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == "Hulaki")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _meters.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Record(instrument, value, tags));
        _meters.Start();
    }

    public IReadOnlyList<Activity> Spans => [.. _spans];

    public IReadOnlyList<CapturedMeasurement> Measurements => [.. _measurements];

    public IEnumerable<string> AllText() =>
        Spans.SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}").Append(s.DisplayName).Append(s.StatusDescription ?? string.Empty))
            .Concat(Measurements.SelectMany(m => m.Tags.Select(t => $"{t.Key}={t.Value}")));

    public void Dispose()
    {
        _activities.Dispose();
        _meters.Dispose();
    }

    private void Record(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags) =>
        _measurements.Enqueue(new CapturedMeasurement(instrument.Name, instrument.Unit, value, tags.ToArray().ToDictionary(t => t.Key, t => t.Value)));
}

internal sealed record CapturedMeasurement(string Instrument, string? Unit, double Value, IReadOnlyDictionary<string, object?> Tags);
