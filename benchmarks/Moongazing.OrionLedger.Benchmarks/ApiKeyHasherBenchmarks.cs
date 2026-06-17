using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionLedger.Keys;

namespace Moongazing.OrionLedger.Benchmarks;

/// <summary>
/// Measures the storage and verification hashing path: SHA-256 hex digest of a token and the
/// constant-time comparison used so verification does not leak how much of a hash matched.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class ApiKeyHasherBenchmarks
{
    private const string Prefix = "ork_live_";
    private string token = string.Empty;
    private string hash = string.Empty;
    private string sameHash = string.Empty;
    private string differentHash = string.Empty;

    [GlobalSetup]
    public void Setup()
    {
        token = ApiKeyGenerator.Generate(Prefix, 32);
        hash = ApiKeyHasher.Hash(token);
        sameHash = ApiKeyHasher.Hash(token);
        differentHash = ApiKeyHasher.Hash(ApiKeyGenerator.Generate(Prefix, 32));
    }

    [Benchmark]
    public string Hash()
    {
        return ApiKeyHasher.Hash(token);
    }

    [Benchmark]
    public bool FixedTimeEquals_Match()
    {
        return ApiKeyHasher.FixedTimeEquals(hash, sameHash);
    }

    [Benchmark]
    public bool FixedTimeEquals_Mismatch()
    {
        return ApiKeyHasher.FixedTimeEquals(hash, differentHash);
    }
}
