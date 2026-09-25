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
| Slack, Teams | webhook send renders bold and links |
| Bluesky | post with a link and Nepali text; link facet is clickable; delete it |
| Mastodon | post twice with the same idempotency key; one status exists |

## Tag and publish

1. `git tag v<version>` and push the tag.
2. The release workflow runs preflight, pack, verify. Open the `verify` artifact and check each
   `.nupkg` dependency group against PLAN.md section 5.2.
3. Approve the `production` deployment.

## After publishing

- Install the new version into a scratch console from nuget.org and run one Telegram send.
- If a package is broken: unlist that version on nuget.org (never delete), fix forward with a
  patch release. Consumers pinned to the broken version keep working; new restores skip it.
