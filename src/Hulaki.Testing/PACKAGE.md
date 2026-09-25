# Hulaki.Testing

Test helpers for [Hulaki](https://github.com/sanamhub/hulaki), for applications that send through
Hulaki and for authors of provider packages.

**Status: in development.** The API can change until 1.0.

| Type | Use |
| --- | --- |
| `FakeChannel` | An `IChannel` that records every send and returns the outcome you choose. No network. |
| `ScriptedHttpHandler` | An `HttpMessageHandler` that answers from a script and records each request with its body. |
| `ChannelContractTests<TChannel>` | xUnit v3 facts every provider must pass: `Prepare` does no I/O, a refused connection is retried, errors do not echo content, cancellation throws. |

A provider test project derives one class. `MyChannel` and its options stand for your provider:

```csharp
public sealed class MyContractTests : ChannelContractTests<MyChannel>
{
    protected override Recipient ValidRecipient { get; } = new("test-recipient-0001");

    protected override MyChannel Create(HttpMessageHandler handler, TimeProvider time) =>
        new("my", new HttpClient(handler), new MyChannelOptions { ApiKey = "TEST-key", TimeProvider = time });

    protected override HttpResponseMessage Success() => new(HttpStatusCode.OK) { Content = new StringContent("{}") };
}
```

The package references `xunit.v3.extensibility.core`, not `xunit.v3`, so your test project keeps
its own runner. Source, issues and license (MIT): https://github.com/sanamhub/hulaki
