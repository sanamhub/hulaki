using System;
using System.Collections.Generic;
using System.Linq;
using Hulaki.Text;

namespace Hulaki;

/// <summary>An operation or content feature a channel may offer (ADR-0006).</summary>
public enum Capability
{
    /// <summary>Plain text body. Every channel declares it.</summary>
    Text = 0,

    /// <summary>A separate title field.</summary>
    Title = 1,

    /// <summary>Bold, italic, code and links rendered natively.</summary>
    Markup = 2,

    /// <summary>Image attachments.</summary>
    Images = 3,

    /// <summary>Video attachments.</summary>
    Video = 4,

    /// <summary>Priority mapped to something the recipient notices.</summary>
    Priority = 5,

    /// <summary>A click action or link field separate from the body.</summary>
    ClickAction = 6,

    /// <summary>The platform deduplicates a repeated send by key, so a send can be retried safely after an unknown outcome.</summary>
    IdempotentSend = 7,

    /// <summary>The channel can look up what happened to a send whose outcome was unknown.</summary>
    Reconcile = 8,

    /// <summary>A sent message can be deleted.</summary>
    Delete = 9,
}

/// <summary>Whether a <see cref="Capability"/> can be used, and if not, why.</summary>
public enum Availability
{
    /// <summary>Usable now.</summary>
    Available = 0,

    /// <summary>The platform has no such feature.</summary>
    UnsupportedByPlatform = 1,

    /// <summary>The platform has it and this channel does not implement it yet.</summary>
    NotImplemented = 2,

    /// <summary>Needs a scope or permission the credential may not have.</summary>
    PermissionRequired = 3,

    /// <summary>Needs the platform to approve the app (app review, business verification).</summary>
    ApprovalDependent = 4,

    /// <summary>Needs a paid plan or per-message billing.</summary>
    Paid = 5,

    /// <summary>Depends on the account or instance, and is only known when a request is made.</summary>
    UnknownUntilRequest = 6,
}

/// <summary>One row of a <see cref="CapabilityManifest"/>.</summary>
/// <param name="Capability">The capability.</param>
/// <param name="Availability">Whether it can be used.</param>
/// <param name="Notes">Short human note, shown in generated docs and by <c>hulaki capabilities</c>.</param>
public sealed record CapabilityDeclaration(Capability Capability, Availability Availability, string? Notes = null);

/// <summary>
/// What a channel can do, as data. Checked before any network call and used to generate the
/// capability matrix in the docs (ADR-0006).
/// </summary>
public sealed class CapabilityManifest
{
    private readonly Dictionary<Capability, CapabilityDeclaration> _byCapability;

    /// <summary>Creates a manifest.</summary>
    /// <param name="platform">Platform id, lower case, for example <c>telegram</c>.</param>
    /// <param name="textLimit">Maximum body length and how it is counted.</param>
    /// <param name="declarations">Declared capabilities. Undeclared ones count as <see cref="Availability.NotImplemented"/>.</param>
    /// <param name="maxAttachments">Maximum attachments per message. Zero when media is not supported.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="platform"/> is empty, or a capability is declared twice.</exception>
    public CapabilityManifest(string platform, TextLimit textLimit, IEnumerable<CapabilityDeclaration> declarations, int maxAttachments = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentNullException.ThrowIfNull(textLimit);
        ArgumentNullException.ThrowIfNull(declarations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAttachments);

        Platform = platform;
        TextLimit = textLimit;
        MaxAttachments = maxAttachments;
        _byCapability = [];
        foreach (var declaration in declarations)
        {
            if (!_byCapability.TryAdd(declaration.Capability, declaration))
            {
                throw new ArgumentException($"Capability {declaration.Capability} is declared twice.", nameof(declarations));
            }
        }

        Declarations = [.. _byCapability.Values.OrderBy(d => d.Capability)];
    }

    /// <summary>Platform id, lower case.</summary>
    public string Platform { get; }

    /// <summary>Maximum body length and how it is counted.</summary>
    public TextLimit TextLimit { get; }

    /// <summary>Maximum attachments per message.</summary>
    public int MaxAttachments { get; }

    /// <summary>All declarations, ordered by capability.</summary>
    public IReadOnlyList<CapabilityDeclaration> Declarations { get; }

    /// <summary>Returns the availability of a capability. Undeclared means <see cref="Availability.NotImplemented"/>.</summary>
    /// <param name="capability">The capability.</param>
    /// <returns>The availability.</returns>
    public Availability Get(Capability capability) =>
        _byCapability.TryGetValue(capability, out var d) ? d.Availability : Availability.NotImplemented;

    /// <summary>True when <see cref="Get"/> returns <see cref="Availability.Available"/>.</summary>
    /// <param name="capability">The capability.</param>
    /// <returns>Whether it can be used without further checks.</returns>
    public bool Supports(Capability capability) => Get(capability) == Availability.Available;
}
