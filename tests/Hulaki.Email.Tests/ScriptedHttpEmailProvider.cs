using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using FreeTierMail;

namespace Hulaki.Email.Tests;

/// <summary>
/// Adapts the HTTP-scripted contract kit to the email channel: an HTTP email provider that posts
/// each message to the scripted handler. A 2xx answer is accepted; any other answer is a refusal
/// whose reason is the response body, so the kit's "errors do not echo content" check reaches the
/// channel's mapping.
/// </summary>
internal sealed class ScriptedHttpEmailProvider(HttpClient http) : HttpEmailProvider(http, new ScriptedOptions(), "scripted")
{
    protected override HttpRequestMessage CreateRequest(EmailMessage message) =>
        new(HttpMethod.Post, new Uri("https://mail.test/send")) { Content = new StringContent(message.Subject) };

    protected override ProviderResult MapResponse(HttpStatusCode status, HttpResponseHeaders headers, string body) =>
        (int)status is >= 200 and < 300
            ? ProviderResult.Accepted("scripted-1")
            : ProviderResult.Of(ProviderOutcome.RecipientRejected, body);

    private sealed class ScriptedOptions : EmailProviderOptions
    {
        public ScriptedOptions() => ApiKey = "test-key-0000000000000000";
    }
}
