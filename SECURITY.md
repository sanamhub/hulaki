# Security policy

## Supported versions

Only the latest release on nuget.org receives fixes. While Hulaki is 0.x, that is the latest
prerelease.

## Reporting a vulnerability

Report privately through
[GitHub security advisories](https://github.com/sanamhub/hulaki/security/advisories/new). Do not open a public issue.

Include the smallest code that shows it, the package versions and the .NET version. Expect a
first reply within a week. This is a one-person project, so a fix can take longer; the advisory
says when one is ready.

Security bugs here include: a token, webhook URL, recipient address or message text reaching a
log, trace, metric or exception message; a request sent to a host other than the configured one;
Web Push encryption that does not match RFC 8291; a retry that resends a message the platform may
already have accepted when the caller did not opt in (ADR-0005).

A platform changing its API is a normal bug. Open an issue.
