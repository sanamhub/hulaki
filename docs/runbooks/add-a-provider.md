# Runbook: add a provider

For contributors and agents adding a channel. A channel with no new dependency goes into the
`Hulaki` package under `src/Hulaki/<Name>/`; one that needs a new dependency gets its own package
like `Hulaki.Email`, and an ADR. Read ADR-0005, ADR-0006, ADR-0007, ADR-0009
and ADR-0015 first.

## 1. Before writing code

| Check | Where it goes |
| --- | --- |
| Official API docs for the send endpoint and its errors | links in `docs/channels/<name>.md` and the test file header |
| Terms of service allow automated sending | a note in the PR; stop if unclear |
| Cost and approval gates | manifest `Availability` (`Paid`, `ApprovalDependent`) |
| Text limit and how it is counted | `TextLimit` with the right `TextCounter` |
| Where the secret travels (header, path, body) | if in the URL, note it in the options XML docs and add the host to the README's tracing filter |
| Published rate limits | default `RateLimiter` in the channel |
| Does the platform deduplicate by key? | declare `IdempotentSend` only if documented |

## 2. Project

```
src/Hulaki/<Name>/             namespace Hulaki.<Name>
  <Name>Channel.cs             sealed, derives from ChannelBase, static Manifest
  <Name>ChannelOptions.cs      derives from ChannelOptions
  <Name>HulakiBuilderExtensions.cs
  Wire/                        internal request and response types, JsonSerializerContext
docs/channels/<name>.md        the channel's facts table, linked from src/Hulaki/PACKAGE.md
tests/Hulaki.Tests/<Name>/
  Fixtures/                    JSON copied from the platform docs, synthetic values only
  <Name>ChannelTests.cs
  <Name>ContractTests.cs       derives from Hulaki.Testing.ChannelContractTests<T>
```

Public members go into `src/Hulaki/PublicAPI.Unshipped.txt`.

## 3. Rules

- `SendOnceAsync` maps every platform answer to an outcome and lets `HttpRequestException` and
  timeouts escape.
- Read error bodies defensively: an HTML page from a proxy must not throw.
- `HulakiError.Message` is your own words. Put the platform's code in `PlatformCode`.
- Map "user blocked us" and "recipient gone" to `RecipientBlocked` and `RecipientNotFound`.
- Render markup yourself from `MarkupDocument` and escape every text node for the platform.
- No reflection JSON. No new dependency without an ADR.

## 4. Tests

Success; each documented error; 429 with the platform's own retry hint; an HTML 502; an empty
200; markup rendering with characters that need escaping; the contract kit.

## 5. Finish

- Add the live test to [release.md](release.md).
- Regenerate `docs/capabilities.md` with `hulaki capabilities --markdown`.
- Add the channel to the tables in `src/Hulaki/PACKAGE.md` and the README.
