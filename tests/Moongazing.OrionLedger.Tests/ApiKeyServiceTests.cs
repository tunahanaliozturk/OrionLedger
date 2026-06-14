namespace Moongazing.OrionLedger.Tests;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

using Xunit;

public sealed class ApiKeyServiceTests
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

    [Fact]
    public async Task Issue_then_verify_succeeds_and_marks_last_used()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:write"]);

        f.Clock = f.Clock.AddMinutes(5);
        var verification = await f.Service.VerifyAsync(issued.Token);

        Assert.True(verification.IsValid);
        Assert.Equal(issued.Record.Id, verification.Record!.Id);
        Assert.Equal(f.Clock, verification.Record.LastUsedAt);
    }

    [Fact]
    public async Task The_plaintext_token_is_not_recoverable_from_the_record()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        var stored = await f.Store.FindByIdAsync(issued.Record.Id);

        Assert.NotNull(stored);
        Assert.DoesNotContain(issued.Token, stored!.Hash, StringComparison.Ordinal);
        Assert.Equal(ApiKeyHasher.Hash(issued.Token), stored.Hash);
    }

    [Fact]
    public async Task A_malformed_token_is_rejected()
    {
        using var f = new Fixture();

        Assert.Equal(ApiKeyStatus.Malformed, (await f.Service.VerifyAsync(null)).Status);
        Assert.Equal(ApiKeyStatus.Malformed, (await f.Service.VerifyAsync("")).Status);
        Assert.Equal(ApiKeyStatus.Malformed, (await f.Service.VerifyAsync("wrong_prefix_token")).Status);
    }

    [Fact]
    public async Task An_unknown_token_is_not_found()
    {
        using var f = new Fixture();
        var verification = await f.Service.VerifyAsync("ork_unknownsecretvalue");

        Assert.Equal(ApiKeyStatus.NotFound, verification.Status);
    }

    [Fact]
    public async Task An_expired_key_fails_verification()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", expiresAt: f.Clock.AddMinutes(10));

        f.Clock = f.Clock.AddMinutes(11);
        var verification = await f.Service.VerifyAsync(issued.Token);

        Assert.Equal(ApiKeyStatus.Expired, verification.Status);
        Assert.Equal(issued.Record.Id, verification.Record!.Id);
    }

    [Fact]
    public async Task The_default_lifetime_is_applied_when_no_expiry_is_given()
    {
        using var f = new Fixture(new ApiKeyOptions { DefaultLifetime = TimeSpan.FromMinutes(30) });
        var issued = await f.Service.IssueAsync("acme");

        Assert.Equal(f.Clock.AddMinutes(30), issued.Record.ExpiresAt);
    }

    [Fact]
    public async Task A_revoked_key_fails_verification()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        var revoked = await f.Service.RevokeAsync(issued.Record.Id);
        var verification = await f.Service.VerifyAsync(issued.Token);

        Assert.True(revoked);
        Assert.Equal(ApiKeyStatus.Revoked, verification.Status);
    }

    [Fact]
    public async Task Revoking_an_unknown_or_already_revoked_key_returns_false()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme");

        Assert.False(await f.Service.RevokeAsync("nope"));
        Assert.True(await f.Service.RevokeAsync(issued.Record.Id));
        Assert.False(await f.Service.RevokeAsync(issued.Record.Id));
    }

    [Fact]
    public async Task A_required_scope_must_be_present()
    {
        using var f = new Fixture();
        var issued = await f.Service.IssueAsync("acme", ["orders:read"]);

        Assert.True((await f.Service.VerifyAsync(issued.Token, "orders:read")).IsValid);
        Assert.Equal(ApiKeyStatus.MissingScope,
            (await f.Service.VerifyAsync(issued.Token, "orders:write")).Status);
    }

    [Fact]
    public async Task Empty_name_is_rejected()
    {
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Service.IssueAsync(""));
    }
}
