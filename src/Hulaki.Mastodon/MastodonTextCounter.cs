using System.Text.RegularExpressions;
using Hulaki.Text;

namespace Hulaki.Mastodon;

/// <summary>
/// Counts as Mastodon's StatusLengthValidator does: grapheme clusters, with every URL counted as
/// 23 characters. Mastodon also counts a remote mention <c>@user@domain</c> as <c>@user</c>; that
/// rule is left out, so a status with remote mentions counts more here than on the server, never
/// less, and a prefix never counts more than the whole.
/// </summary>
internal sealed partial class MastodonTextCounter : TextCounter
{
    public static MastodonTextCounter Instance { get; } = new();

    public override string Unit => "characters (URLs 23)";

    public override int Count(string text) => Graphemes.Count(Url().Replace(text, new string('x', 23)));

    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.CultureInvariant)]
    private static partial Regex Url();
}
