# Runbook: release

For the maintainer. Semver, changelog, green CI, approval, rollback plan,
post-deploy verification.

## Before tagging

1. `main` is green. `PublicAPI.Unshipped.txt` is empty in every project (move entries to
   `Shipped`).
2. `CHANGELOG.md` has a section for the new version; `<Version>` in `Directory.Build.props`
   matches.
3. Run the live checklist below with your test accounts. Secrets come from your environment;
   never paste them into a terminal that records history to a shared place.

| Provider | Live check |
| --- | --- |
| Telegram | send markup with `<`, `&` and Devanagari to the test group; message renders correctly |
| Discord | webhook send; `@everyone` in the text does not ping |
| ntfy | send to the random topic with priority High and a click link; phone shows it |
| Web Push | subscribe in Chrome and Firefox via the sample page; both receive; unsubscribe gives `RecipientNotFound` on the next send |
| Email | send to the test mailbox; HTML and text parts both present |
| Webhook | send with `Secret` set to a local receiver; its HMAC-SHA256 of the raw body matches `X-Hulaki-Signature` |
| Slack, Teams | webhook send renders bold and links; in Teams, `snake_case` in plain text shows no backslash |
| Bluesky | post with a link and Nepali text; link facet is clickable; delete it |
| Mastodon | post twice with the same idempotency key; one status exists |

## One-time setup

1. Settings > Environments: create `production` with yourself as required reviewer.
2. nuget.org > Trusted Publishing: add a policy for `sanamhub/hulaki`, workflow `release.yml`,
   environment `production`.
3. Settings > Secrets and variables > Actions: add the secret `NUGET_USER` (the nuget.org account
   name the policy belongs to; it is not a key).
4. Run `release` from the Actions tab with `dry-run` ticked. It builds, verifies and uploads
   `release-files` without publishing. The SBOM step's `-fn` flag is unchecked until this run:
   compare it with `dotnet CycloneDX --help` in the log.

## Tag and publish

1. `git tag v<version>` and push the tag.
2. The release workflow runs preflight, then verify. Open the `release-files` artifact and check
   each `.nupkg` dependency group: nothing beyond the packages in `Directory.Packages.props`.
3. Approve the `production` deployment. The core is pushed first, then the rest.
4. `verify-published` then downloads every package from nuget.org and consumes it, plain and
   under Native AOT. It waits up to 45 minutes for indexing. If it fails on a timeout alone, run
   it again from the Actions tab with the tag.

## After publishing

- `verify-published` is green. Then install the new version into a scratch console from
  nuget.org and run one Telegram send.
- If a package is broken: unlist that version on nuget.org (never delete), fix forward with a
  patch release. Consumers pinned to the broken version keep working; new restores skip it.
