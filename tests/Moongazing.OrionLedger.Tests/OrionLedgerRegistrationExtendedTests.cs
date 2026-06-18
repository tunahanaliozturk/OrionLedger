namespace Moongazing.OrionLedger.Tests;

using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Observers;
using Moongazing.OrionLedger.Storage;

using Xunit;

/// <summary>
/// Extended DI registration coverage: respect for pre-registered components, observer wiring, and
/// a full issue/verify/revoke round trip resolved entirely from the container.
/// </summary>
public sealed class OrionLedgerRegistrationExtendedTests
{
    private sealed class CustomStore : IApiKeyStore
    {
        private readonly InMemoryApiKeyStore inner = new();

        public Task AddAsync(ApiKeyRecord record, CancellationToken cancellationToken = default) =>
            inner.AddAsync(record, cancellationToken);

        public Task<ApiKeyRecord?> FindByHashAsync(string hash, CancellationToken cancellationToken = default) =>
            inner.FindByHashAsync(hash, cancellationToken);

        public Task<ApiKeyRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default) =>
            inner.FindByIdAsync(id, cancellationToken);

        public Task UpdateAsync(ApiKeyRecord record, CancellationToken cancellationToken = default) =>
            inner.UpdateAsync(record, cancellationToken);
    }

    [Fact]
    public void A_pre_registered_store_is_not_overridden()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IApiKeyStore, CustomStore>();
        services.AddOrionLedger();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomStore>(provider.GetRequiredService<IApiKeyStore>());
    }

    [Fact]
    public async Task A_registered_observer_is_passed_to_the_service()
    {
        var observer = new RecordingApiKeyEventObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IApiKeyEventObserver>(observer);
        services.AddOrionLedger();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApiKeyService>();

        await service.IssueAsync("acme");
        Assert.Single(observer.Issued);
    }

    [Fact]
    public void The_diagnostics_and_options_are_registered_as_singletons()
    {
        var services = new ServiceCollection();
        services.AddOrionLedger();

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<ApiKeyDiagnostics>(),
            provider.GetRequiredService<ApiKeyDiagnostics>());
        Assert.Same(
            provider.GetRequiredService<ApiKeyOptions>(),
            provider.GetRequiredService<ApiKeyOptions>());
    }

    [Fact]
    public void The_service_is_registered_as_a_singleton()
    {
        var services = new ServiceCollection();
        services.AddOrionLedger();

        using var provider = services.BuildServiceProvider();
        Assert.Same(
            provider.GetRequiredService<IApiKeyService>(),
            provider.GetRequiredService<IApiKeyService>());
    }

    [Fact]
    public void AddOrionLedger_rejects_a_null_service_collection()
    {
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddOrionLedger());
    }

    [Fact]
    public async Task A_full_lifecycle_runs_through_the_resolved_service()
    {
        var services = new ServiceCollection();
        services.AddOrionLedger(o => o.Prefix = "live_");

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApiKeyService>();

        var issued = await service.IssueAsync("acme", ["orders:read"]);
        Assert.StartsWith("live_", issued.Token, StringComparison.Ordinal);

        Assert.True((await service.VerifyAsync(issued.Token, "orders:read")).IsValid);
        Assert.Equal(ApiKeyStatus.MissingScope,
            (await service.VerifyAsync(issued.Token, "orders:write")).Status);

        Assert.True(await service.RevokeAsync(issued.Record.Id));
        Assert.Equal(ApiKeyStatus.Revoked, (await service.VerifyAsync(issued.Token)).Status);
    }
}
