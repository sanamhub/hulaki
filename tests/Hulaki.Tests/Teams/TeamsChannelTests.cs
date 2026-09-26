using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Hulaki.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Hulaki.Teams.Tests;

/// <summary>
/// The card follows the Workflows template "Post to a channel when a webhook request is received"
/// and https://adaptivecards.io/explorer/TextBlock.html. Error bodies follow the Logic Apps shape
/// <c>{"error":{"code":...,"message":...}}</c>. The URL is synthetic.
/// </summary>
public sealed class TeamsChannelTests
{
    internal const string Workflow = "https://prod-00.westeurope.logic.azure.com:443/workflows/0000aaaa/triggers/manual/paths/invoke?api-version=2016-06-01&sp=%2Ftriggers%2Fmanual%2Frun&sv=1.0&sig=TESTsignature000000000";

    private static (TeamsChannel Channel, ScriptedHttpHandler Stub) Create()
    {
        var stub = new ScriptedHttpHandler();
        var channel = new TeamsChannel("teams", new HttpClient(stub), new TeamsChannelOptions { WorkflowUrl = new Uri(Workflow), TimeProvider = new FakeTimeProvider() });
        return (channel, stub);
    }

    [Fact]
    public async Task Posts_an_adaptive_card_and_202_is_accepted()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.Accepted, string.Empty);
        var message = new Message("**Red** in _Myagdi_ [DHM](https://dhm.gov.np) `code_1`")
        {
            Format = TextFormat.Markup,
            Title = "Route alert",
            Link = new Uri("https://example.org/watch/42"),
        };

        var outcome = await channel.SendAsync(message, Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Accepted, outcome.Status);
        var request = Assert.Single(stub.Requests);
        Assert.Equal(new Uri(Workflow), request.Request.RequestUri);
        var root = JsonDocument.Parse(request.Body).RootElement;
        Assert.Equal("message", root.GetProperty("type").GetString());
        var attachment = root.GetProperty("attachments")[0];
        Assert.Equal("application/vnd.microsoft.card.adaptive", attachment.GetProperty("contentType").GetString());
        var card = attachment.GetProperty("content");
        Assert.Equal("AdaptiveCard", card.GetProperty("type").GetString());
        var body = card.GetProperty("body");
        Assert.Equal("Route alert", body[0].GetProperty("text").GetString());
        Assert.Equal("Bolder", body[0].GetProperty("weight").GetString());
        Assert.Equal(@"**Red** in _Myagdi_ [DHM](https://dhm.gov.np/) code\_1", body[1].GetProperty("text").GetString());
        Assert.True(body[1].GetProperty("wrap").GetBoolean());
        var action = card.GetProperty("actions")[0];
        Assert.Equal("Action.OpenUrl", action.GetProperty("type").GetString());
        Assert.Equal("https://example.org/watch/42", action.GetProperty("url").GetString());
    }

    [Fact]
    public void Plain_text_is_escaped_and_there_is_no_title_or_action()
    {
        var card = TeamsChannel.Card(new Message(@"snake_case *x* [y] \z"));

        var content = card.Attachments[0].Content;
        Assert.Null(content.Actions);
        Assert.Equal(@"snake\_case \*x\* \[y\] \\z", Assert.Single(content.Body).Text);
    }

    [Theory]
    [InlineData("https://outlook.office.com/webhook/0000/IncomingWebhook/1111/2222")]
    [InlineData("https://contoso.webhook.office.com/webhookb2/0000/IncomingWebhook/1111/2222")]
    public void Retired_connector_urls_are_refused_with_directions(string address)
    {
        var ex = Assert.Throws<ArgumentException>(() => new TeamsChannel("teams", new HttpClient(), new TeamsChannelOptions { WorkflowUrl = new Uri(address) }));

        Assert.Contains("retired", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Workflows", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("IncomingWebhook", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_http_url_is_refused()
    {
        Assert.Throws<ArgumentException>(() => new TeamsChannel("teams", new HttpClient(), new TeamsChannelOptions { WorkflowUrl = new Uri("http://prod-00.westeurope.logic.azure.com/workflows/x") }));
    }

    public static TheoryData<HttpStatusCode, string, HulakiErrorCode, string?> Errors() => new()
    {
        { HttpStatusCode.BadRequest, """{"error":{"code":"TriggerInputSchemaMismatch","message":"The input body for trigger 'manual' of type 'Request' did not match its schema definition."}}""", HulakiErrorCode.InvalidInput, "TriggerInputSchemaMismatch" },
        { HttpStatusCode.Unauthorized, """{"error":{"code":"DirectApiInvalidAuthorizationSignature","message":"The provided authorization signature is not valid."}}""", HulakiErrorCode.InvalidConfiguration, "DirectApiInvalidAuthorizationSignature" },
        { HttpStatusCode.NotFound, """{"error":{"code":"WorkflowNotFound","message":"The workflow could not be found."}}""", HulakiErrorCode.InvalidConfiguration, "WorkflowNotFound" },
        { HttpStatusCode.RequestEntityTooLarge, "<html>too large</html>", HulakiErrorCode.InvalidInput, null },
    };

    [Theory]
    [MemberData(nameof(Errors))]
    public async Task Errors_map_by_status_and_keep_only_the_code(HttpStatusCode status, string body, HulakiErrorCode code, string? platformCode)
    {
        var (channel, stub) = Create();
        stub.Respond(status, body);

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Failed, outcome.Status);
        Assert.Equal(code, outcome.Error!.Code);
        Assert.Equal(platformCode, outcome.Error.PlatformCode);
        Assert.DoesNotContain("schema definition", outcome.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_429_is_rate_limited_and_retried()
    {
        var (channel, stub) = Create();
        stub.Respond(_ =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(600));
                return response;
            });

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(HulakiErrorCode.RateLimited, outcome.Error!.Code);
        Assert.Equal(TimeSpan.FromSeconds(600), outcome.Error.RetryAfter);
    }

    [Fact]
    public async Task Html_502_is_unknown()
    {
        var (channel, stub) = Create();
        stub.Respond(HttpStatusCode.BadGateway, "<html>bad gateway</html>", "text/html");

        var outcome = await channel.SendAsync(new Message("x"), Recipient.Self, TestContext.Current.CancellationToken);

        Assert.Equal(DeliveryStatus.Unknown, outcome.Status);
    }

    [Fact]
    public void The_limit_counts_the_whole_card()
    {
        var (channel, _) = Create();

        Assert.Empty(channel.Prepare(new Message(new string('a', 27_000)), Recipient.Self));
        Assert.Contains(channel.Prepare(new Message(new string('a', 27_900)), Recipient.Self), i => i.Code == "text-too-long");
        Assert.Contains(channel.Prepare(new Message("x"), new Recipient("someone")), i => i.Code == "recipient-must-be-self");
    }

    [Fact]
    public void Options_bind_and_validation_explains_a_connector_url()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["teams:WorkflowUrl"] = Workflow }).Build();
        var services = new ServiceCollection();
        services.AddHulaki()
            .AddTeams("teams", configuration.GetSection("teams"))
            .AddTeams("old", o => o.WorkflowUrl = new Uri("https://outlook.office.com/webhook/TESTsecret"));
        using var provider = services.BuildServiceProvider();

        Assert.Equal(new Uri(Workflow), provider.GetRequiredService<IOptionsMonitor<TeamsChannelOptions>>().Get("teams").WorkflowUrl);
        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IStartupValidator>().Validate());
        Assert.Contains("Teams channel 'old'", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("TESTsecret", ex.Message, StringComparison.Ordinal);
    }
}

public sealed class TeamsContractTests : ChannelContractTests<TeamsChannel>
{
    protected override Recipient ValidRecipient => Recipient.Self;

    protected override TeamsChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("teams", new HttpClient(handler), new TeamsChannelOptions { WorkflowUrl = new Uri(TeamsChannelTests.Workflow), TimeProvider = time });

    protected override HttpResponseMessage Success() => new(HttpStatusCode.Accepted);
}
