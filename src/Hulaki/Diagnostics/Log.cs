using System;
using Microsoft.Extensions.Logging;

namespace Hulaki.Diagnostics;

/// <summary>
/// Every log event Hulaki writes, ids 1 to 7 of the reserved 1 to 20. Parameters are channel
/// names, platform ids, error codes, counts and times only. No message text, title, link,
/// recipient, token or platform description, at any level (ADR-0012).
/// </summary>
internal static partial class Log
{
    [LoggerMessage(EventId = 1, EventName = "SendDelivered", Level = LogLevel.Debug,
        Message = "Send on channel {Channel} ({Platform}) succeeded after {Attempts} attempts.")]
    public static partial void SendDelivered(ILogger logger, string channel, string platform, int attempts);

    [LoggerMessage(EventId = 2, EventName = "Retrying", Level = LogLevel.Information,
        Message = "Send on channel {Channel} ({Platform}) failed with {ErrorCode}; retrying in {Delay}.")]
    public static partial void Retrying(ILogger logger, string channel, string platform, HulakiErrorCode errorCode, TimeSpan delay);

    [LoggerMessage(EventId = 3, EventName = "SendFailed", Level = LogLevel.Warning,
        Message = "Send on channel {Channel} ({Platform}) failed with {ErrorCode}.")]
    public static partial void SendFailed(ILogger logger, string channel, string platform, HulakiErrorCode errorCode);

    [LoggerMessage(EventId = 4, EventName = "OutcomeUnknown", Level = LogLevel.Warning,
        Message = "Send on channel {Channel} ({Platform}) has an unknown outcome: the request went out and no answer came back.")]
    public static partial void OutcomeUnknown(ILogger logger, string channel, string platform);

    [LoggerMessage(EventId = 5, EventName = "ChannelPaused", Level = LogLevel.Information,
        Message = "Channel {Channel} is paused until {Until} after a rate limit.")]
    public static partial void ChannelPaused(ILogger logger, string channel, DateTimeOffset until);

    [LoggerMessage(EventId = 6, EventName = "IdempotencyConflict", Level = LogLevel.Warning,
        Message = "Idempotency key reused with different content; nothing sent to {ChannelCount} channels.")]
    public static partial void IdempotencyConflict(ILogger logger, int channelCount);

    // The exception object is not logged: its message and data can carry what the provider sent.
    [LoggerMessage(EventId = 7, EventName = "ProviderThrew", Level = LogLevel.Error,
        Message = "Channel {Channel} threw {ExceptionType}; its target failed with UpstreamFailure.")]
    public static partial void ProviderThrew(ILogger logger, string channel, string exceptionType);
}
