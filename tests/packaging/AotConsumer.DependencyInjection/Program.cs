using System.Net;
using System.Text;
using Hulaki;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// The host path: configuration binding, ValidateOnStart and the named HttpClient, answered by a stub.
var configuration = new ConfigurationBuilder()
    .AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Hulaki:Channels:alerts:BotToken"] = "123456:TEST-token_0000000000000000000",
        ["Hulaki:Channels:alerts:Retry:ResendUnknown"] = "true",
    })
    .Build();

var services = new ServiceCollection();
services.AddHulaki().AddTelegram("alerts", configuration.GetSection("Hulaki:Channels:alerts"));
services.AddHttpClient("hulaki.alerts").ConfigurePrimaryHttpMessageHandler(() => new TelegramSuccess());
await using var provider = services.BuildServiceProvider();
provider.GetRequiredService<IStartupValidator>().Validate();

var client = provider.GetRequiredService<HulakiClient>();
var result = await client.SendAsync(new Message("Rain warning for Myagdi"), [new Target("alerts", new Recipient("-1001234567890"))]);
var outcome = result.Outcomes[0].Outcome;

Console.WriteLine(outcome.Status);
return outcome.Status == DeliveryStatus.Delivered ? 0 : 1;

internal sealed class TelegramSuccess : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"ok":true,"result":{"message_id":42}}""", Encoding.UTF8, "application/json"),
        });
}
