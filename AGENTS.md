# Working in this repository

Instructions for anyone, human or AI agent, who changes code or docs here. `CLAUDE.md` imports
this file.

## What this is

Hulaki, an MIT-licensed .NET 10 library that delivers messages to chat, push and social platforms
through one API. The design is [docs/PLAN.md](docs/PLAN.md) and [docs/adr](docs/adr). The build
order is [docs/IMPLEMENTATION.md](docs/IMPLEMENTATION.md): take the lowest-numbered task that is
not done, finish it completely, and stop.

The design documents (`docs/PLAN.md`, `docs/IMPLEMENTATION.md`, `docs/adr`, `docs/research`,
`docs/reference`) are kept by the maintainer and are not in the public repository. If they are
missing from your checkout, ask the maintainer rather than guessing.

## Commands

```bash
dotnet build -c Release
```

```bash
dotnet test -c Release
```

```bash
dotnet pack -c Release -o artifacts/packages
```

A task is done only when all three pass with zero warnings on your machine, before you open the
PR. Warnings are errors. Do not add `NoWarn` or `#pragma` to silence one unless the task says
so, and then with a one-line reason.

## Hard rules

1. **No real secrets or personal data anywhere**: code, tests, fixtures, logs, issues, PR text,
   commits, or prompts to another tool. Tokens in tests look like
   `123456:TEST-token_0000000000000000000`. Live tests read secrets from the environment only.
2. **Hulaki never logs, traces or puts in an error message** a token, a webhook URL, a recipient
   address or message text (ADR-0012, ADR-0015). If you are unsure whether a string is safe to
   log, it is not.
3. **The public API is PLAN.md section 3, exactly.** Everything else is `internal`. Stop and ask
   before adding a public member.
4. **No new package references** beyond PLAN.md section 5.2 without an ADR.
5. **Copy reference code, do not retype it.** `docs/reference` compiles under the final settings.
6. **`ConfigureAwait(false)` on every await in `src`.**
7. **Document every exception** each public method can throw with `<exception cref>`.
8. **Providers let `HttpRequestException` and timeouts escape `SendOnceAsync`.** `ChannelBase`
   classifies them (ADR-0005). Never catch them in a provider.
9. **Follow the writing rules** below for docs, comments, commits and PR descriptions.

## Commits and pull requests

- Conventional Commits, one task per PR, named after the task, for example
  `feat(telegram): T08 telegram provider`.
- No AI attribution trailers in commits or PRs (PLAN.md Q5).
- The PR description ticks each "Done when" item and pastes the last lines of the three commands.

## Writing

The full rules are in [.claude/skills/writing-style/SKILL.md](.claude/skills/writing-style/SKILL.md).
Agents that do not load skills follow this short form:

- No em dashes. None of the banned filler words listed in the skill.
- Numbers over adjectives. Say what was verified and how; label a guess as a guess.
- Commits: `type(scope): summary`, imperative, lower case, under 72 characters, body says why.
- **No AI attribution** (`Co-Authored-By`, `Generated with`) in commits, PRs or files.
- PR description: an opening paragraph, a bullet per area, then **Not in this PR** and
  **Verifying it** when they apply.

## Project standards

A personal project of [@sanamhub](https://github.com/sanamhub), not company work. These are the
defaults; a deviation needs an ADR.

| Area | Standard |
| --- | --- |
| Decisions | Significant decisions are ADRs in `docs/adr`. Accepted ADRs are superseded, never edited. |
| Requirements | A task starts only when its acceptance criteria are written and testable (PLAN.md). Legal, security and privacy questions are asked, never assumed. |
| Tests | Unit over integration over end to end; a test per acceptance criterion; 80 percent line coverage target; no real personal data. |
| Security | OWASP ASVS where it applies; TLS 1.2 or later; secrets only in environment variables or GitHub environment secrets; CI blocks high and critical advisories. |
| Release | SemVer and `CHANGELOG.md`; green CI; the `production` environment approval; rollback is unlist plus a patch release. |
| Review | One maintainer merges their own PRs after full CI. PRs touching credentials, retries, Web Push crypto or the release wait 24 hours and get a `/code-review` pass first. This ends when a second maintainer joins. |
| Layering | Domain and Application share the `Hulaki` assembly; each provider package is Infrastructure; there is no Presentation layer. Providers depend on the core, never the reverse (ADR-0003). |
| Operations | A library has no environment to provision. Consumers get monitoring through `ActivitySource` and `Meter` named `Hulaki` (ADR-0012). |