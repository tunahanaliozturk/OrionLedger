namespace Moongazing.OrionLedger.Keys;

/// <summary>Why a verification succeeded or failed.</summary>
public enum ApiKeyStatus
{
    /// <summary>The key is known, active, unexpired, and (if checked) holds the required scope.</summary>
    Valid,

    /// <summary>The token did not match the expected prefix or was empty.</summary>
    Malformed,

    /// <summary>No key with this token hash exists.</summary>
    NotFound,

    /// <summary>The key exists but has passed its expiry.</summary>
    Expired,

    /// <summary>The key exists but was revoked.</summary>
    Revoked,

    /// <summary>The key is otherwise valid but lacks a scope that was required.</summary>
    MissingScope,
}

/// <summary>
/// The outcome of verifying a presented token: the status, and the matched record when the token
/// resolved to a known key (even if it was expired or revoked).
/// </summary>
public sealed class ApiKeyVerification
{
    private ApiKeyVerification(ApiKeyStatus status, ApiKeyRecord? record)
    {
        Status = status;
        Record = record;
    }

    /// <summary>The verification result.</summary>
    public ApiKeyStatus Status { get; }

    /// <summary>
    /// The matched record, present whenever the token resolved to a known key (status
    /// <see cref="ApiKeyStatus.Valid"/>, <see cref="ApiKeyStatus.Expired"/>,
    /// <see cref="ApiKeyStatus.Revoked"/>, or <see cref="ApiKeyStatus.MissingScope"/>).
    /// </summary>
    public ApiKeyRecord? Record { get; }

    /// <summary>True only when <see cref="Status"/> is <see cref="ApiKeyStatus.Valid"/>.</summary>
    public bool IsValid => Status == ApiKeyStatus.Valid;

    internal static ApiKeyVerification Valid(ApiKeyRecord record) => new(ApiKeyStatus.Valid, record);

    internal static ApiKeyVerification Malformed { get; } = new(ApiKeyStatus.Malformed, null);

    internal static ApiKeyVerification NotFound { get; } = new(ApiKeyStatus.NotFound, null);

    internal static ApiKeyVerification Expired(ApiKeyRecord record) => new(ApiKeyStatus.Expired, record);

    internal static ApiKeyVerification Revoked(ApiKeyRecord record) => new(ApiKeyStatus.Revoked, record);

    internal static ApiKeyVerification MissingScope(ApiKeyRecord record) => new(ApiKeyStatus.MissingScope, record);
}
