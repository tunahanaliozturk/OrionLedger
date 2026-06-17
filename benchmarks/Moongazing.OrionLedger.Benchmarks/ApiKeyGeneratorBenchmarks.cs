using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using Moongazing.OrionLedger.Keys;

namespace Moongazing.OrionLedger.Benchmarks;

/// <summary>
/// Measures token generation, the cryptographically random hot path at issuance:
/// drawing random bytes and base64url-encoding them, across a range of secret sizes.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net80)]
[SimpleJob(RuntimeMoniker.Net90)]
public class ApiKeyGeneratorBenchmarks
{
    private const string Prefix = "ork_live_";
    private string token = string.Empty;

    /// <summary>Secret entropy in bytes. 16 is the floor; 32 (256 bits) is the library default.</summary>
    [Params(16, 32, 64)]
    public int SecretByteLength { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        token = ApiKeyGenerator.Generate(Prefix, SecretByteLength);
    }

    [Benchmark]
    public string Generate()
    {
        return ApiKeyGenerator.Generate(Prefix, SecretByteLength);
    }

    [Benchmark]
    public string DisplayPrefix()
    {
        return ApiKeyGenerator.DisplayPrefix(token);
    }
}
