namespace Moongazing.OrionLedger;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Observers;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// Registration helpers for OrionLedger.
/// </summary>
public static class OrionLedgerServiceCollectionExtensions
{
    /// <summary>
    /// Register the key service, options, diagnostics, and an <see cref="InMemoryApiKeyStore"/>.
    /// To persist keys, register your own <see cref="IApiKeyStore"/> before this call; the
    /// in-memory store is only added if none is present. Register an
    /// <see cref="IApiKeyEventObserver"/> to receive lifecycle events.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional issuance configuration.</param>
    public static IServiceCollection AddOrionLedger(
        this IServiceCollection services,
        Action<ApiKeyOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new ApiKeyOptions();
        configure?.Invoke(options);
        options.Validate();

        services.TryAddSingleton(options);
        services.TryAddSingleton<ApiKeyDiagnostics>();
        services.TryAddSingleton<IApiKeyStore, InMemoryApiKeyStore>();

        services.TryAddSingleton<IApiKeyService>(sp => new ApiKeyService(
            sp.GetRequiredService<IApiKeyStore>(),
            sp.GetRequiredService<ApiKeyOptions>(),
            sp.GetRequiredService<ApiKeyDiagnostics>(),
            sp.GetService<IApiKeyEventObserver>()));

        return services;
    }
}
