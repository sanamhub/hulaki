# Capabilities

What each Hulaki provider supports, generated from its capability manifest by
`hulaki capabilities --markdown`. Do not edit it by hand: CI fails when this file and the
command disagree. A capability a provider does not declare reads as not implemented.

| | bluesky | discord | email | ntfy | slack | teams | telegram | webhook | webpush |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Text limit | 300 graphemes | 2,000 utf16 | 1,000,000 utf16 | 4,096 bytes | 40,000 utf16 | 28,000 bytes | 4,096 utf16 | 65,536 bytes | 3,993 bytes |
| Attachments | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 |
| Text | available | available | available | available | available | available | available | available | available |
| Title | unsupported by platform | not implemented | available | available | not implemented | available | unsupported by platform | available | available |
| Markup | available | available | available | available | available | available | available | unsupported by platform | unsupported by platform |
| Images | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented |
| Video | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented |
| Priority | unsupported by platform | unsupported by platform | available | available | unsupported by platform | unsupported by platform | available | available | available |
| ClickAction | unsupported by platform | unsupported by platform | unsupported by platform | available | unsupported by platform | available | not implemented | available | available |
| IdempotentSend | unsupported by platform | unsupported by platform | unsupported by platform | unsupported by platform | unsupported by platform | unsupported by platform | unsupported by platform | unsupported by platform | unsupported by platform |
| Reconcile | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented |
| Delete | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented | not implemented |

## bluesky

| Capability | Availability | Note |
| --- | --- | --- |
| Text | available | Longer text can become a thread (ThreadLongPosts) |
| Title | unsupported by platform | Sent as the first line |
| Markup | available | Links become facets; bold, italic and code are sent as plain text |
| ClickAction | unsupported by platform | The link is appended as a facet |

## discord

| Capability | Availability | Note |
| --- | --- | --- |
| Title | not implemented | Sent as a bold first line; embeds are not used |
| Markup | available | Discord Markdown, text escaped |
| ClickAction | unsupported by platform | The link is appended |

## email

| Capability | Availability | Note |
| --- | --- | --- |
| Title | available | The subject; without a title, the first line of the text |
| Markup | available | Rendered into the HTML part |
| Priority | available | Priority, Importance and X-Priority headers |
| ClickAction | unsupported by platform | The link is appended |

## ntfy

| Capability | Availability | Note |
| --- | --- | --- |
| Markup | available | Markdown; rendered by the web app, shown as text where a client does not render it |
| Priority | available | Low 2, Normal 3, High 4, Urgent 5 |
| ClickAction | available | Message.Link opens on tap |

## slack

| Capability | Availability | Note |
| --- | --- | --- |
| Title | not implemented | Sent as a bold first line; blocks are not used |
| Markup | available | mrkdwn: *bold*, _italic_, `code`, <url\|text> |
| ClickAction | unsupported by platform | The link is appended |

## teams

| Capability | Availability | Note |
| --- | --- | --- |
| Title | available | A bold TextBlock above the text |
| Markup | available | Adaptive Card Markdown; code spans are sent as text |
| ClickAction | available | An Open link button |

## telegram

| Capability | Availability | Note |
| --- | --- | --- |
| Title | unsupported by platform | Sent as a bold first line |
| Markup | available | Telegram HTML parse mode |
| Images | not implemented | sendPhoto in 0.2 |
| Priority | available | Low sends silently; others notify |

## webhook

| Capability | Availability | Note |
| --- | --- | --- |
| Title | available | The title field |
| Markup | unsupported by platform | Sent as plain text |
| Priority | available | The priority field; the receiver decides what it means |
| ClickAction | available | The link field |

## webpush

| Capability | Availability | Note |
| --- | --- | --- |
| Title | available | The payload's title field |
| Markup | unsupported by platform | Notifications show plain text |
| Priority | available | Urgency header: Low very-low, Normal normal, High and Urgent high |
| ClickAction | available | The payload's url field, for the service worker |
