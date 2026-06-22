namespace Moongazing.OrionLedger.EntityFrameworkCore;

using System.ComponentModel;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Moongazing.OrionLedger.Storage;

/// <summary>
/// Registration helpers for the EF Core <see cref="IApiKeyStore"/>.
/// </summary>
public static class OrionLedgerEntityFrameworkCoreServiceCollectionExtensions
{
    /// <summary>
    /// Register <see cref="EfApiKeyStore{TContext}"/> as the <see cref="IApiKeyStore"/>, backed by a
    /// context factory configured here. Call this <em>before</em> <c>AddOrionLedger()</c>: that
    /// method only adds the in-memory store when no <see cref="IApiKeyStore"/> is present, so
    /// registering the EF store first makes it win.
    /// </summary>
    /// <remarks>
    /// This registers a pooled <see cref="IDbContextFactory{TContext}"/> from
    /// <paramref name="configureContext"/>, which the store uses to open a short-lived context per
    /// operation. A per-operation context is what makes a single store instance safe under concurrent
    /// verifications, because a <see cref="DbContext"/> is not thread-safe. Use the bundled
    /// <see cref="OrionLedgerDbContext"/>, or any context that maps the key entity through
    /// <see cref="ApiKeyRecordConfiguration"/>. The store itself is registered as a singleton because
    /// it holds no per-request state and resolves its own contexts.
    /// </remarks>
    /// <typeparam name="TContext">The context type that maps the key entity.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="configureContext">Configures the context (provider, connection string, and so on).</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddOrionLedgerEntityFrameworkCoreStore<TContext>(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureContext)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureContext);

        services.AddPooledDbContextFactory<TContext>(configureContext);
        services.TryAddSingleton<IApiKeyStore, EfApiKeyStore<TContext>>();
        return services;
    }

    /// <summary>
    /// Register <see cref="EfApiKeyStore{TContext}"/> as the <see cref="IApiKeyStore"/> when an
    /// <see cref="IDbContextFactory{TContext}"/> is already registered (for example you called
    /// <c>AddDbContextFactory</c> or <c>AddPooledDbContextFactory</c> yourself). Call it
    /// <em>before</em> <c>AddOrionLedger()</c>.
    /// </summary>
    /// <typeparam name="TContext">The context type that maps the key entity.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static IServiceCollection AddOrionLedgerEntityFrameworkCoreStore<TContext>(
        this IServiceCollection services)
        where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<IApiKeyStore, EfApiKeyStore<TContext>>();
        return services;
    }
}
