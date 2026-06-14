namespace Moongazing.OrionLedger.Tests;

using Microsoft.Extensions.DependencyInjection;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Storage;

using Xunit;

public sealed class OrionLedgerRegistrationTests
{
    [Fact]
    public async Task AddOrionLedger_resolves_a_working_service()
    {
        var services = new ServiceCollection();
        services.AddOrionLedger();

        using var provider = services.BuildServiceProvider();
        var service = provider.GetRequiredService<IApiKeyService>();

        var issued = await service.IssueAsync("acme");
        Assert.True((await service.VerifyAsync(issued.Token)).IsValid);
    }

    [Fact]
    public void AddOrionLedger_registers_the_in_memory_store_by_default()
    {
        var services = new ServiceCollection();
        services.AddOrionLedger();

        using var provider = services.BuildServiceProvider();
        Assert.IsType<InMemoryApiKeyStore>(provider.GetService<IApiKeyStore>());
    }

    [Fact]
    public void AddOrionLedger_honours_configured_options()
    {
        var services = new ServiceCollection();
        services.AddOrionLedger(o => o.Prefix = "live_");

        using var provider = services.BuildServiceProvider();
        Assert.Equal("live_", provider.GetRequiredService<ApiKeyOptions>().Prefix);
    }

    [Fact]
    public void AddOrionLedger_rejects_invalid_options_eagerly()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddOrionLedger(o => o.SecretByteLength = 4));
    }
}
