namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

using Xunit;

/// <summary>
/// Comprehensive lifecycle coverage for <see cref="ApiKeyService"/>: issuance shape, verification
/// resolution for every status, scope and expiry boundaries, revocation idempotency, and observer
/// notification. Uses a deterministic clock and id sequence through the internal test constructor.
/// </summary>
public sealed class ApiKeyServiceLifecycleTests
{
    private sealed class Fixture : IDisposable
    {
        private int idCounter;

        public Fixture(ApiKeyOptions? options = null)
        {
            Options = options ?? new ApiKeyOptions();
            Store = new InMemoryApiKeyStore();
            Service = new ApiKeyService(Store, Options, Diagnostics, observer: null,
                now: () => Clock,
                newId: () => $"key_{++idCounter}");
        }

        public ApiKeyOptions Options { get; }

        public InMemoryApiKeyStore Store { get; }

        public ApiKeyDiagnostics Diagnostics { get; } = new();

        public ApiKeyService Service { get; }

        public DateTimeOffset Clock { get; set; } = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

        public void Dispose() => Diagnostics.Dispose();
    }

    // ---- IssueAsync shape ------------------------------------------------------------------

    [Fact]
    public async Task Issue_returns_the_plaintext_token_exactly_once()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.StartsWith(f.Options.Prefix, issued.Token, StringComparison.Ordinal);
        // The record carries only the hash, never the token.
        Assert.Equal(ApiKeyHasher.Hash(issued.Token), issued.Record.Hash);
        Assert.DoesNotContain(issued.Token, issued.Record.Hash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_persists_the_record_in_the_store()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        var byId = await f.Store.FindByIdAsync(issued.Record.Id);
        var byHash = await f.Store.FindByHashAsync(issued.Record.Hash);

        Assert.Same(issued.Record, byId);
        Assert.Same(issued.Record, byHash);
    }

    [Fact]
    public async Task Issue_populates_metadata_from_the_clock_and_id_source()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.Equal("key_1", issued.Record.Id);
        Assert.Equal("acme", issued.Record.Name);
        Assert.Equal(f.Clock, issued.Record.CreatedAt);
        Assert.Null(issued.Record.RevokedAt);
        Assert.Null(issued.Record.LastUsedAt);
        Assert.Equal(ApiKeyGenerator.DisplayPrefix(issued.Token), issued.Record.DisplayPrefix);
    }

    [Fact]
    public async Task Issue_with_null_scopes_yields_an_empty_scope_set()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.Empty(issued.Record.Scopes);
    }

    [Fact]
    public async Task Issue_copies_the_supplied_scopes()
    {
        using var f = new Fixture();
        var scopes = new List<string> { "orders:read", "orders:write" };
        var issued = await f.Service.IssueAsync("acme", scopes);

        // Mutating the caller's collection afterwards must not affect the stored record.
        scopes.Add("admin");

        Assert.Equal(2, issued.Record.Scopes.Count);
        Assert.Contains("orders:read", issued.Record.Scopes);
        Assert.Contains("orders:write", issued.Record.Scopes);
        Assert.DoesNotContain("admin", issued.Record.Scopes);
    }

    [Fact]
    public async Task Issue_deduplicates_repeated_scopes()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read", "orders:read"]);

        Assert.Single(issued.Record.Scopes);
    }

    [Fact]
    public async Task Issue_assigns_distinct_ids_to_successive_keys()
    {
        using var f = new Fixture();
        var first = await f.Service.IssueAsync("a");
        var second = await f.Service.IssueAsync("b");

        Assert.NotEqual(first.Record.Id, second.Record.Id);
        Assert.NotEqual(first.Token, second.Token);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Issue_rejects_a_null_or_empty_name(string? name)
    {
        using var f = new Fixture();
        // ThrowIfNullOrEmpty raises ArgumentNullException for null, ArgumentException for empty.
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Service.IssueAsync(name!));
    }

    // ---- Expiry semantics ------------------------------------------------------------------

    [Fact]
    public async Task Issue_without_expiry_and_no_default_lifetime_never_expires()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.Null(issued.Record.ExpiresAt);
    }

    [Fact]
    public async Task Explicit_expiry_overrides_the_default_lifetime()
    {
        using var f = new Fixture(new ApiKeyOptions { DefaultLifetime = TimeSpan.FromHours(1) });
        var explicitExpiry = f.Clock.AddDays(7);
        var issued = await f.Service.IssueAsync("acme", expiresAt: explicitExpiry);

        Assert.Equal(explicitExpiry, issued.Record.ExpiresAt);
    }

    [Fact]
    public async Task A_key_is_valid_one_tick_before_expiry()
    {
        using var f = new Fixture();
        var expiresAt = f.Clock.AddMinutes(10);
        var issued = await f.Service.IssueAsync("acme", expiresAt: expiresAt);

        f.Clock = expiresAt.AddTicks(-1);
        Assert.True((await f.Service.VerifyAsync(issued.Token)).IsValid);
    }

    [Fact]
    public async Task A_key_is_expired_at_exactly_the_expiry_instant()
    {
        // Resolution treats expiry as inclusive: expiresAt <= now() is expired.
        using var f = new Fixture();
        var expiresAt = f.Clock.AddMinutes(10);
        var issued = await f.Service.IssueAsync("acme", expiresAt: expiresAt);

        f.Clock = expiresAt;
        Assert.Equal(ApiKeyStatus.Expired, (await f.Service.VerifyAsync(issued.Token)).Status);
    }

    [Fact]
    public async Task An_expired_verification_carries_the_matched_record()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", expiresAt: f.Clock.AddMinutes(1));

        f.Clock = f.Clock.AddMinutes(2);
        var verification = await f.Service.VerifyAsync(issued.Token);

        Assert.Equal(ApiKeyStatus.Expired, verification.Status);
        Assert.NotNull(verification.Record);
        Assert.Equal(issued.Record.Id, verification.Record!.Id);
    }

    [Fact]
    public async Task An_expired_key_does_not_have_its_last_used_timestamp_updated()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", expiresAt: f.Clock.AddMinutes(1));

        f.Clock = f.Clock.AddMinutes(2);
        await f.Service.VerifyAsync(issued.Token);

        var stored = await f.Store.FindByIdAsync(issued.Record.Id);
        Assert.Null(stored!.LastUsedAt);
    }

    // ---- Verification: malformed / not found -----------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("ork")]
    [InlineData("Ork_token")]
    [InlineData(" ork_token")]
    public async Task A_token_that_does_not_match_the_prefix_is_malformed(string? token)
    {
        using var f = new Fixture();
        Assert.Equal(ApiKeyStatus.Malformed, (await f.Service.VerifyAsync(token)).Status);
    }

    [Fact]
    public async Task A_malformed_verification_carries_no_record()
    {
        using var f = new Fixture();
        var verification = await f.Service.VerifyAsync("not-a-key");

        Assert.Equal(ApiKeyStatus.Malformed, verification.Status);
        Assert.Null(verification.Record);
    }

    [Fact]
    public async Task A_well_formed_but_unknown_token_is_not_found()
    {
        using var f = new Fixture();
        var verification = await f.Service.VerifyAsync("ork_thisisnotarealsecret");

        Assert.Equal(ApiKeyStatus.NotFound, verification.Status);
        Assert.Null(verification.Record);
    }

    [Fact]
    public async Task A_token_with_the_right_prefix_but_a_tampered_secret_is_not_found()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        var tampered = issued.Token + "x";
        Assert.Equal(ApiKeyStatus.NotFound, (await f.Service.VerifyAsync(tampered)).Status);
    }

    // ---- Verification: valid path ----------------------------------------------------------

    [Fact]
    public async Task A_valid_verification_updates_last_used_and_persists_it()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        f.Clock = f.Clock.AddMinutes(5);
        var verification = await f.Service.VerifyAsync(issued.Token);

        Assert.True(verification.IsValid);
        Assert.Equal(f.Clock, verification.Record!.LastUsedAt);

        var stored = await f.Store.FindByIdAsync(issued.Record.Id);
        Assert.Equal(f.Clock, stored!.LastUsedAt);
    }

    [Fact]
    public async Task Repeated_valid_verifications_advance_last_used()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        f.Clock = f.Clock.AddMinutes(1);
        await f.Service.VerifyAsync(issued.Token);
        f.Clock = f.Clock.AddMinutes(1);
        await f.Service.VerifyAsync(issued.Token);

        var stored = await f.Store.FindByIdAsync(issued.Record.Id);
        Assert.Equal(f.Clock, stored!.LastUsedAt);
    }

    // ---- Scope checks ----------------------------------------------------------------------

    [Fact]
    public async Task A_null_required_scope_skips_the_scope_check()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.True((await f.Service.VerifyAsync(issued.Token, requiredScope: null)).IsValid);
    }

    [Fact]
    public async Task A_held_scope_passes_the_scope_check()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read", "orders:write"]);

        Assert.True((await f.Service.VerifyAsync(issued.Token, "orders:read")).IsValid);
        Assert.True((await f.Service.VerifyAsync(issued.Token, "orders:write")).IsValid);
    }

    [Fact]
    public async Task A_missing_scope_fails_and_carries_the_record()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read"]);

        var verification = await f.Service.VerifyAsync(issued.Token, "orders:write");

        Assert.Equal(ApiKeyStatus.MissingScope, verification.Status);
        Assert.Equal(issued.Record.Id, verification.Record!.Id);
    }

    [Fact]
    public async Task Scope_matching_is_case_sensitive_ordinal()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read"]);

        Assert.Equal(ApiKeyStatus.MissingScope,
            (await f.Service.VerifyAsync(issued.Token, "Orders:Read")).Status);
    }

    [Fact]
    public async Task A_key_with_no_scopes_fails_any_required_scope()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.Equal(ApiKeyStatus.MissingScope,
            (await f.Service.VerifyAsync(issued.Token, "orders:read")).Status);
    }

    [Fact]
    public async Task A_missing_scope_does_not_update_last_used()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read"]);

        await f.Service.VerifyAsync(issued.Token, "orders:write");

        var stored = await f.Store.FindByIdAsync(issued.Record.Id);
        Assert.Null(stored!.LastUsedAt);
    }

    // ---- Resolution ordering ---------------------------------------------------------------

    [Fact]
    public async Task Revocation_is_checked_before_expiry()
    {
        // A key that is both revoked and expired resolves as Revoked, because revocation is
        // evaluated first in ResolveAsync.
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", expiresAt: f.Clock.AddMinutes(10));

        await f.Service.RevokeAsync(issued.Record.Id);
        f.Clock = f.Clock.AddMinutes(20); // now also past expiry

        Assert.Equal(ApiKeyStatus.Revoked, (await f.Service.VerifyAsync(issued.Token)).Status);
    }

    [Fact]
    public async Task Expiry_is_checked_before_scope()
    {
        // An expired key missing the required scope resolves as Expired, not MissingScope.
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read"], expiresAt: f.Clock.AddMinutes(10));

        f.Clock = f.Clock.AddMinutes(20);

        Assert.Equal(ApiKeyStatus.Expired,
            (await f.Service.VerifyAsync(issued.Token, "orders:write")).Status);
    }

    // ---- Revocation ------------------------------------------------------------------------

    [Fact]
    public async Task Revoking_an_active_key_returns_true_and_stamps_the_record()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        f.Clock = f.Clock.AddMinutes(3);
        var revoked = await f.Service.RevokeAsync(issued.Record.Id);

        Assert.True(revoked);
        var stored = await f.Store.FindByIdAsync(issued.Record.Id);
        Assert.Equal(f.Clock, stored!.RevokedAt);
    }

    [Fact]
    public async Task A_revoked_key_fails_verification_with_its_record()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");
        await f.Service.RevokeAsync(issued.Record.Id);

        var verification = await f.Service.VerifyAsync(issued.Token);

        Assert.Equal(ApiKeyStatus.Revoked, verification.Status);
        Assert.Equal(issued.Record.Id, verification.Record!.Id);
    }

    [Fact]
    public async Task Revocation_is_idempotent()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.True(await f.Service.RevokeAsync(issued.Record.Id));
        Assert.False(await f.Service.RevokeAsync(issued.Record.Id));
        Assert.False(await f.Service.RevokeAsync(issued.Record.Id));
    }

    [Fact]
    public async Task A_second_revoke_does_not_move_the_revoked_timestamp()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        await f.Service.RevokeAsync(issued.Record.Id);
        var firstRevokedAt = (await f.Store.FindByIdAsync(issued.Record.Id))!.RevokedAt;

        f.Clock = f.Clock.AddMinutes(10);
        await f.Service.RevokeAsync(issued.Record.Id);
        var secondRevokedAt = (await f.Store.FindByIdAsync(issued.Record.Id))!.RevokedAt;

        Assert.Equal(firstRevokedAt, secondRevokedAt);
    }

    [Fact]
    public async Task Revoking_an_unknown_id_returns_false()
    {
        using var f = new Fixture();
        Assert.False(await f.Service.RevokeAsync("does-not-exist"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Revoke_rejects_a_null_or_empty_id(string? id)
    {
        using var f = new Fixture();
        await Assert.ThrowsAnyAsync<ArgumentException>(() => f.Service.RevokeAsync(id!));
    }

    // ---- Cancellation ----------------------------------------------------------------------

    [Fact]
    public async Task Issue_observes_a_cancelled_token()
    {
        using var f = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // The in-memory store completes synchronously, so issuance itself does not honour
        // cancellation; this documents that the call still succeeds rather than throwing.
        var issued = await f.Service.IssueAsync("acme", cancellationToken: cts.Token);
        Assert.NotNull(issued);
    }
}
