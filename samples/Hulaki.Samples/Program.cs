using Hulaki.Samples;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

// Compiled by CI, not run: running it needs a real bot token. To try it, set
// Hulaki__Channels__alerts__BotToken in the environment and pass "send".
var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
if (args is ["send"])
{
    await SendSample.RunAsync(configuration);
}
else
{
    using var tracing = TracingSample.Build();
    using var provider = DependencyInjectionSample.Register(new ServiceCollection(), configuration).BuildServiceProvider();
    Console.WriteLine("Samples compiled. Pass \"send\" with a bot token to send one message.");
}
