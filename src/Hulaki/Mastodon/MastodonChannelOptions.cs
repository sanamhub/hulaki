using System;
using Hulaki.Channels;

namespace Hulaki.Mastodon;

/// <summary>Options for <see cref="MastodonChannel"/>.</summary>
public sealed class MastodonChannelOptions : ChannelOptions
{
    /// <summary>The account's instance, for example <c>https://mastodon.social/</c>.</summary>
    public Uri? InstanceUrl { get; set; }

    /// <summary>An access token with the <c>write:statuses</c> scope. A secret.</summary>
    public string AccessToken { get; set; } = string.Empty;

    /// <summary><c>public</c>, <c>unlisted</c>, <c>private</c> or <c>direct</c>. Null uses the account's default.</summary>
    public string? Visibility { get; set; }

    /// <summary>ISO 639 language of the statuses, for example <c>ne</c>. Null lets the instance guess.</summary>
    public string? Language { get; set; }

    /// <summary>
    /// The instance's character limit, when it allows more than Mastodon's default 500. Null
    /// checks against 500 before sending and reads the instance's own limit from
    /// <c>/api/v2/instance</c> on the first send; a lower instance limit then applies too.
    /// </summary>
    public int? MaxCharacters { get; set; }
}
