namespace Moongazing.OrionLedger.AspNetCore.Tests;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// Builds a real <see cref="ApiKeyService"/> over an in-memory store so tests exercise the genuine
/// verify path (hash lookup, revocation, expiry, last-used update) rather than a mock.
/// </summary>
internal sealed class ApiKeyTestHarness
{
    public ApiKeyTestHarness()
    {
        Store = new InMemoryApiKeyStore();
        Service = new ApiKeyService(Store, new ApiKeyOptions(), new ApiKeyDiagnostics());
    }

    public InMemoryApiKeyStore Store { get; }

    public IApiKeyService Service { get; }

    public async Task<IssuedApiKey> IssueAsync(
        string name = "test",
        IEnumerable<string>? scopes = null,
        DateTimeOffset? expiresAt = null,
        string? subject = null)
        => await Service.IssueAsync(name, scopes, expiresAt, subject);

    public async Task<ApiKeyRecord?> FindByIdAsync(string id)
        => await Store.FindByIdAsync(id);
}
