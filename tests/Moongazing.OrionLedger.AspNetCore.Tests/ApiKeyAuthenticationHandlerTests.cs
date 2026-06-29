namespace Moongazing.OrionLedger.AspNetCore.Tests;

using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using Moongazing.OrionLedger;

public sealed class ApiKeyAuthenticationHandlerTests
{
    private static async Task<(AuthenticateResult Result, HttpContext Context)> AuthenticateAsync(
        IApiKeyService service,
        Action<HttpContext> configureRequest,
        Action<ApiKeyAuthenticationOptions>? configureOptions = null)
    {
        var options = new ApiKeyAuthenticationOptions();
        configureOptions?.Invoke(options);

        var handler = new ApiKeyAuthenticationHandler(
            new StaticOptionsMonitor(options),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            service);

        var scheme = new AuthenticationScheme(
            ApiKeyAuthenticationOptions.DefaultScheme,
            displayName: null,
            typeof(ApiKeyAuthenticationHandler));

        var context = new DefaultHttpContext();
        configureRequest(context);

        await handler.InitializeAsync(scheme, context);
        var result = await handler.AuthenticateAsync();
        return (result, context);
    }

    [Fact]
    public async Task valid_key_authenticates_with_subject_and_scopes()
    {
        var harness = new ApiKeyTestHarness();
        var issued = await harness.IssueAsync(
            scopes: new[] { "orders:read", "orders:write" },
            subject: "tenant-42");

        var (result, _) = await AuthenticateAsync(
            harness.Service,
            ctx => ctx.Request.Headers["X-Api-Key"] = issued.Token);

        Assert.True(result.Succeeded);
        var principal = result.Principal!;
        Assert.Equal("tenant-42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal("tenant-42", principal.Identity!.Name);
        Assert.Equal(
            issued.Record.Id,
            principal.FindFirstValue("orionledger:key-id"));

        var scopes = principal.FindAll("scope").Select(c => c.Value).OrderBy(s => s).ToArray();
        Assert.Equal(new[] { "orders:read", "orders:write" }, scopes);
    }

    [Fact]
    public async Task revoked_key_is_rejected_without_principal()
    {
        var harness = new ApiKeyTestHarness();
        var issued = await harness.IssueAsync(scopes: new[] { "orders:read" });
        await harness.Service.RevokeAsync(issued.Record.Id);

        var (result, _) = await AuthenticateAsync(
            harness.Service,
            ctx => ctx.Request.Headers["X-Api-Key"] = issued.Token);

        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task expired_key_is_rejected_without_principal()
    {
        var harness = new ApiKeyTestHarness();
        var issued = await harness.IssueAsync(
            expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var (result, _) = await AuthenticateAsync(
            harness.Service,
            ctx => ctx.Request.Headers["X-Api-Key"] = issued.Token);

        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task unknown_key_is_rejected_without_principal()
    {
        var harness = new ApiKeyTestHarness();

        var (result, _) = await AuthenticateAsync(
            harness.Service,
            ctx => ctx.Request.Headers["X-Api-Key"] = "ork_not-a-real-key");

        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
    }

    [Fact]
    public async Task missing_header_yields_no_result()
    {
        var harness = new ApiKeyTestHarness();

        var (result, _) = await AuthenticateAsync(harness.Service, _ => { });

        Assert.False(result.Succeeded);
        Assert.Null(result.Principal);
        Assert.True(result.None);
    }

    [Fact]
    public async Task successful_verification_updates_last_used()
    {
        var harness = new ApiKeyTestHarness();
        var issued = await harness.IssueAsync(scopes: new[] { "orders:read" });
        Assert.Null(issued.Record.LastUsedAt);
        Assert.Equal(0, issued.Record.LastUsedCount);

        var (result, _) = await AuthenticateAsync(
            harness.Service,
            ctx => ctx.Request.Headers["X-Api-Key"] = issued.Token);

        Assert.True(result.Succeeded);
        var stored = await harness.FindByIdAsync(issued.Record.Id);
        Assert.NotNull(stored!.LastUsedAt);
        Assert.Equal(1, stored.LastUsedCount);
    }

    [Fact]
    public async Task custom_header_name_is_honoured()
    {
        var harness = new ApiKeyTestHarness();
        var issued = await harness.IssueAsync(scopes: new[] { "orders:read" });

        var (result, _) = await AuthenticateAsync(
            harness.Service,
            ctx => ctx.Request.Headers["Authorization-Key"] = issued.Token,
            options => options.HeaderName = "Authorization-Key");

        Assert.True(result.Succeeded);
    }

    private sealed class StaticOptionsMonitor : IOptionsMonitor<ApiKeyAuthenticationOptions>
    {
        private readonly ApiKeyAuthenticationOptions options;

        public StaticOptionsMonitor(ApiKeyAuthenticationOptions options) => this.options = options;

        public ApiKeyAuthenticationOptions CurrentValue => options;

        public ApiKeyAuthenticationOptions Get(string? name) => options;

        public IDisposable? OnChange(Action<ApiKeyAuthenticationOptions, string?> listener) => null;
    }
}
