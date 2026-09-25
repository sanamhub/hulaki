using System;
using Hulaki.Channels;
using Microsoft.Extensions.Configuration;

namespace Hulaki.Telegram;

/// <summary>
/// The configuration-bindable part of <see cref="TelegramChannelOptions"/>. The options class also
/// holds a <see cref="System.Threading.RateLimiting.RateLimiter"/> and a <see cref="TimeProvider"/>,
/// which configuration cannot express and the binding generator rejects (SYSLIB1100), so the
/// section binds to this type and the values are copied across. Null means "keep the default".
/// </summary>
internal sealed class TelegramChannelSettings
{
    public string? BotToken { get; set; }

    public Uri? BaseAddress { get; set; }

    public bool? DisableLinkPreview { get; set; }

    public bool? DisableRateLimiting { get; set; }

    public RetrySettings? Retry { get; set; }

    public static void Apply(IConfiguration section, TelegramChannelOptions options)
    {
        var settings = section.Get<TelegramChannelSettings>();
        if (settings is null)
        {
            return;
        }

        options.BotToken = settings.BotToken ?? options.BotToken;
        options.BaseAddress = settings.BaseAddress ?? options.BaseAddress;
        options.DisableLinkPreview = settings.DisableLinkPreview ?? options.DisableLinkPreview;
        options.DisableRateLimiting = settings.DisableRateLimiting ?? options.DisableRateLimiting;
        if (settings.Retry is { } retry)
        {
            options.Retry = retry.ApplyTo(options.Retry);
        }
    }
}

/// <summary>Bindable form of <see cref="SendRetryPolicy"/>. Null keeps the current value.</summary>
internal sealed class RetrySettings
{
    public int? MaxAttempts { get; set; }

    public TimeSpan? BaseDelay { get; set; }

    public TimeSpan? MaxDelay { get; set; }

    public TimeSpan? MaxRetryAfter { get; set; }

    public bool? ResendUnknown { get; set; }

    public SendRetryPolicy ApplyTo(SendRetryPolicy policy) => policy with
    {
        MaxAttempts = MaxAttempts ?? policy.MaxAttempts,
        BaseDelay = BaseDelay ?? policy.BaseDelay,
        MaxDelay = MaxDelay ?? policy.MaxDelay,
        MaxRetryAfter = MaxRetryAfter ?? policy.MaxRetryAfter,
        ResendUnknown = ResendUnknown ?? policy.ResendUnknown,
    };
}
