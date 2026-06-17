using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Storage;

namespace Moongazing.OrionLedger.Benchmarks;

/// <summary>
/// Measures the end-to-end lifecycle against the process-local in-memory store: issuing a key
/// (generate, hash, persist, emit metric) and the verification resolutions that switch on prefix,
/// hash lookup, expiry, revocation, and scope. No database or external service is involved; the
/// in-memory store completes synchronously, so this isolates the library's own work.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class ApiKeyServiceBenchmarks
{
    private const string Prefix = "ork_live_";
    private const string Scope = "orders:write";

    private ApiKeyDiagnostics diagnostics = null!;
    private ApiKeyService service = null!;
    private string validToken = string.Empty;
    private string malformedToken = string.Empty;
    private string unknownToken = string.Empty;

    [GlobalSetup]
    public async Task Setup()
    {
        diagnostics = new ApiKeyDiagnostics();
        var options = new ApiKeyOptions { Prefix = Prefix, SecretByteLength = 32 };
        service = new ApiKeyService(new InMemoryApiKeyStore(), options, diagnostics);

        var issued = await service.IssueAsync("bench-tenant", scopes: [Scope]);
        validToken = issued.Token;

        // Same prefix so it passes the prefix check but resolves to NotFound on the hash lookup.
        unknownToken = ApiKeyGenerator.Generate(Prefix, 32);

        // Wrong prefix so it short-circuits to Malformed.
        malformedToken = "nope_" + Guid.NewGuid().ToString("N");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        diagnostics.Dispose();
    }

    [Benchmark]
    public IssuedApiKey Issue()
    {
        return service.IssueAsync("bench-tenant", scopes: [Scope]).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ApiKeyVerification Verify_Valid()
    {
        return service.VerifyAsync(validToken, requiredScope: Scope).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ApiKeyVerification Verify_MissingScope()
    {
        return service.VerifyAsync(validToken, requiredScope: "orders:delete").GetAwaiter().GetResult();
    }

    [Benchmark]
    public ApiKeyVerification Verify_NotFound()
    {
        return service.VerifyAsync(unknownToken).GetAwaiter().GetResult();
    }

    [Benchmark]
    public ApiKeyVerification Verify_Malformed()
    {
        return service.VerifyAsync(malformedToken).GetAwaiter().GetResult();
    }
}
