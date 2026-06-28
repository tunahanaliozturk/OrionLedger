namespace Moongazing.OrionLedger.AspNetCore.Tests;

using System.Net;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.AspNetCore.Authorization;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Storage;

/// <summary>
/// Drives the handler through a real ASP.NET Core pipeline (authentication then authorization) with
/// a scope-protected endpoint, using <see cref="TestServer"/> and the in-memory store.
/// </summary>
public sealed class ScopePolicyEndToEndTests
{
    private static async Task<(IHost Host, InMemoryApiKeyStore Store, IApiKeyService Service)> StartHostAsync()
    {
        var store = new InMemoryApiKeyStore();
        var service = new ApiKeyService(store, new ApiKeyOptions(), new ApiKeyDiagnostics());

        var host = await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddSingleton<IApiKeyStore>(store);
                    services.AddSingleton<IApiKeyService>(service);

                    services.AddAuthentication(ApiKeyAuthenticationOptions.DefaultScheme)
                        .AddOrionLedgerApiKey();

                    services.AddAuthorization(options =>
                    {
                        options.AddApiKeyScopePolicy("orders-read", "orders:read");
                    });
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapGet("/orders", () => "orders-ok")
                            .RequireAuthorization("orders-read");
                        endpoints.MapGet("/whoami", (HttpContext ctx) => ctx.User.Identity?.Name ?? "anon")
                            .RequireAuthorization();
                    });
                });
            })
            .StartAsync();

        return (host, store, service);
    }

    [Fact]
    public async Task key_with_scope_is_allowed_on_protected_endpoint()
    {
        var (host, _, service) = await StartHostAsync();
        using var _h = host;

        var issued = await service.IssueAsync("app", new[] { "orders:read" }, subject: "tenant-1");

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", issued.Token);

        var response = await client.GetAsync(new Uri("/orders", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("orders-ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task key_without_scope_is_forbidden_on_protected_endpoint()
    {
        var (host, _, service) = await StartHostAsync();
        using var _h = host;

        // Authenticates (valid key) but lacks orders:read, so authorization forbids: 403, not 401.
        var issued = await service.IssueAsync("app", new[] { "reports:read" }, subject: "tenant-2");

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", issued.Token);

        var response = await client.GetAsync(new Uri("/orders", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task revoked_key_is_unauthorized()
    {
        var (host, _, service) = await StartHostAsync();
        using var _h = host;

        var issued = await service.IssueAsync("app", new[] { "orders:read" });
        await service.RevokeAsync(issued.Record.Id);

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", issued.Token);

        var response = await client.GetAsync(new Uri("/orders", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task no_key_is_unauthorized()
    {
        var (host, _, _) = await StartHostAsync();
        using var _h = host;

        var client = host.GetTestClient();
        var response = await client.GetAsync(new Uri("/whoami", UriKind.Relative));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task authenticated_principal_name_is_the_subject()
    {
        var (host, _, service) = await StartHostAsync();
        using var _h = host;

        var issued = await service.IssueAsync("app", new[] { "orders:read" }, subject: "tenant-9");

        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", issued.Token);

        var name = await client.GetStringAsync(new Uri("/whoami", UriKind.Relative));

        Assert.Equal("tenant-9", name);
    }
}
