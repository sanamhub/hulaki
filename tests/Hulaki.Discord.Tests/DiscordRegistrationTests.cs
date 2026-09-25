using System;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Hulaki.Discord.Tests;

public sealed class DiscordRegistrationTests
{
    [Fact]
    public void Options_bind_from_configuration_and_the_channel_resolves()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Hulaki:Channels:ops:WebhookUrl"] = DiscordChannelTests.Webhook,
            ["Hulaki:Channels:ops:Retry:MaxAttempts"] = "2",
        }).Build();
        var services = new ServiceCollection();
        services.AddHulaki().AddDiscord("ops", configuration.GetSection("Hulaki:Channels:ops"));
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Validate();
        var options = provider.GetRequiredService<IOptionsMonitor<DiscordChannelOptions>>().Get("ops");
        var channel = provider.GetRequiredKeyedService<IChannel>("ops");

        Assert.Equal(new Uri(DiscordChannelTests.Webhook), options.WebhookUrl);
        Assert.Equal(2, options.Retry.MaxAttempts);
        Assert.IsType<DiscordChannel>(channel);
    }

    [Theory]
    [InlineData(null, "WebhookUrl is empty")]
    [InlineData("https://example.org/api/webhooks/1/TEST_secret", "WebhookUrl must be")]
    public void Validate_on_start_fails_on_a_missing_or_foreign_url_without_echoing_it(string? address, string expected)
    {
        var services = new ServiceCollection();
        services.AddHulaki().AddDiscord("ops", o => o.WebhookUrl = address is null ? null : new Uri(address));
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());

        Assert.Contains(expected, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TEST_secret", ex.Message, StringComparison.Ordinal);
    }
}
