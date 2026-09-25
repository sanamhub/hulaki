using System;
using System.Collections.Generic;
using Hulaki.Channels;
using Hulaki.Credentials;

namespace Hulaki.Bluesky;

/// <summary>Options for <see cref="BlueskyChannel"/>.</summary>
public sealed class BlueskyChannelOptions : ChannelOptions
{
    /// <summary>The account's handle, for example <c>alerts.example.org</c>, or its email.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>
    /// An app password from Settings, Privacy and security, App passwords. A secret. Not the
    /// account password: an app password can be revoked on its own.
    /// </summary>
    public string AppPassword { get; set; } = string.Empty;

    /// <summary>The account's server. Defaults to <c>https://bsky.social/</c>.</summary>
    public Uri ServiceUrl { get; set; } = new("https://bsky.social/");

    /// <summary>Languages of the posts as BCP 47 tags, for example <c>ne</c> and <c>en</c>. Empty sends none.</summary>
    public IList<string> Languages { get; } = [];

    /// <summary>
    /// Posts text over 300 graphemes as a thread of replies instead of refusing it. Off by default:
    /// a thread that fails part way leaves the first parts posted.
    /// </summary>
    public bool ThreadLongPosts { get; set; }

    /// <summary>
    /// Where the session is kept between sends, so it is created once and refreshed, not created
    /// per post (<c>createSession</c> allows 30 calls per 5 minutes). Null keeps it in this
    /// channel's memory. Share one store across processes that post as the same account.
    /// </summary>
    public ICredentialStore? CredentialStore { get; set; }
}
