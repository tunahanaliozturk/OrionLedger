namespace Moongazing.OrionLedger.Keys;

/// <summary>
/// The result of rotating a key. Rotation issues a brand-new successor key (its own id, secret, and
/// hash) and supersedes the predecessor. <see cref="Successor"/> carries the new plaintext token,
/// available only here and never again, exactly like a fresh issuance.
/// </summary>
public sealed class KeyRotation
{
    /// <summary>Create a rotation result.</summary>
    /// <param name="successor">The freshly issued successor key, including its plaintext token.</param>
    /// <param name="predecessor">The superseded predecessor record.</param>
    public KeyRotation(IssuedApiKey successor, ApiKeyRecord predecessor)
    {
        ArgumentNullException.ThrowIfNull(successor);
        ArgumentNullException.ThrowIfNull(predecessor);
        Successor = successor;
        Predecessor = predecessor;
    }

    /// <summary>
    /// The new key. Its <see cref="IssuedApiKey.Token"/> is the show-once secret to hand to the
    /// caller; it cannot be recovered later.
    /// </summary>
    public IssuedApiKey Successor { get; }

    /// <summary>
    /// The predecessor record, now superseded. Its <see cref="ApiKeyRecord.SupersededById"/> points
    /// at the successor. When the rotation requested a grace window, the predecessor keeps verifying
    /// until <see cref="ApiKeyRecord.RetiresAt"/>; otherwise it is revoked immediately.
    /// </summary>
    public ApiKeyRecord Predecessor { get; }

    /// <summary>The new plaintext token. A convenience shortcut to <c>Successor.Token</c>.</summary>
    public string Token => Successor.Token;
}
