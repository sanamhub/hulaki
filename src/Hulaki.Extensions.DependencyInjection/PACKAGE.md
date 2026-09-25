# Hulaki.Extensions.DependencyInjection

Registers [Hulaki](https://github.com/sanamhub/hulaki) with `Microsoft.Extensions.DependencyInjection`.

**Status: in development.** The API can change until 1.0.

```csharp
builder.Services.AddHulaki()
    .AddTelegram("alerts", builder.Configuration.GetSection("Hulaki:Channels:alerts"));
```

`AddHulaki()` registers `HulakiClient` as a singleton over every registered channel, and an
in-memory idempotency store unless you register your own `IIdempotencyStore`. Provider packages
add named channels. Each one gets:

- options bound from configuration with the binding source generator, so Native AOT apps work;
- validation at startup (`ValidateOnStart`), so a missing token stops the host instead of the
  first send;
- a named `HttpClient` (`hulaki.<name>`) with a 30 second timeout and **no request logging**,
  because some platforms put the secret in the URL path.

Do not add a retry or resilience handler to these clients. Hulaki retries only where the
platform provably did not act; a generic handler would resend requests that may have landed.

Source, issues and license (MIT): https://github.com/sanamhub/hulaki
