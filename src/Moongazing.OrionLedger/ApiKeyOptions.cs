namespace Moongazing.OrionLedger;

/// <summary>
/// Configuration for key issuance: the human-recognisable prefix, how much entropy each key
/// carries, and the default lifetime.
/// </summary>
public sealed class ApiKeyOptions
{
    /// <summary>
    /// The prefix every issued key starts with, so a leaked key is recognisable in logs and the
    /// environment is obvious. Default <c>ork_</c>. A trailing underscore is conventional.
    /// </summary>
    public string Prefix { get; set; } = "ork_";

    /// <summary>
    /// The number of random bytes in the secret portion of each key. Default 32 (256 bits).
    /// Must be at least 16.
    /// </summary>
    public int SecretByteLength { get; set; } = 32;

    /// <summary>
    /// The default lifetime applied when a key is issued without an explicit expiry. Null means
    /// keys do not expire by default. Default null.
    /// </summary>
    public TimeSpan? DefaultLifetime { get; set; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrEmpty(Prefix);
        if (SecretByteLength < 16)
        {
            throw new ArgumentOutOfRangeException(nameof(SecretByteLength), SecretByteLength,
                "SecretByteLength must be at least 16 for adequate entropy.");
        }
        if (DefaultLifetime is { } lifetime && lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(DefaultLifetime), DefaultLifetime,
                "DefaultLifetime must be positive when set.");
        }
    }
}
