# Benchmarks

Microbenchmarks for OrionLedger's hot paths, built with [BenchmarkDotNet](https://benchmarkdotnet.org/).
They cover only pure, in-memory work: token generation, hashing, constant-time comparison, and the
end-to-end key lifecycle against the process-local `InMemoryApiKeyStore`. Nothing here touches a
database or any external service, so the numbers reflect the library's own cost and nothing else.

The project lives in `benchmarks/Moongazing.OrionLedger.Benchmarks` and is part of the solution. Each
benchmark calls a real public API on the library.

## What is measured

### `ApiKeyGeneratorBenchmarks`
The cryptographically random issuance path. Parameterised by secret entropy (`SecretByteLength` =
16, 32, 64; 32 is the library default of 256 bits).

- `Generate` - draw random bytes and base64url-encode them into a prefixed token.
- `DisplayPrefix` - extract the non-secret leading display prefix from a token.

### `ApiKeyHasherBenchmarks`
The storage and verification hashing path.

- `Hash` - SHA-256 of a token rendered as a lowercase hex digest.
- `FixedTimeEquals_Match` - constant-time comparison of two equal hashes.
- `FixedTimeEquals_Mismatch` - constant-time comparison of two differing hashes (should not
  short-circuit, by design).

### `ApiKeyServiceBenchmarks`
The end-to-end lifecycle through `ApiKeyService` over the in-memory store.

- `Issue` - generate, hash, persist, and emit the issuance metric.
- `Verify_Valid` - resolve a known, active, in-scope key (updates last-used).
- `Verify_MissingScope` - a known key that lacks the required scope.
- `Verify_NotFound` - a well-formed token whose hash matches no stored key.
- `Verify_Malformed` - a token that fails the prefix check and short-circuits.

## Running

From the repository root:

```
dotnet run -c Release --project benchmarks/Moongazing.OrionLedger.Benchmarks
```

Run a single class, or filter by name:

```
dotnet run -c Release --project benchmarks/Moongazing.OrionLedger.Benchmarks -- --filter "*ApiKeyHasherBenchmarks*"
dotnet run -c Release --project benchmarks/Moongazing.OrionLedger.Benchmarks -- --filter "*Verify_Valid*"
```

List everything without running it:

```
dotnet run -c Release --project benchmarks/Moongazing.OrionLedger.Benchmarks -- --list flat
```

Each class carries `[MemoryDiagnoser]` and runs on .NET 8 and .NET 9 via `[SimpleJob]`, so a run
reports time and allocations per operation on both runtimes. Build the benchmark project once with
`dotnet build -c Release` before a run if you want to confirm it compiles in isolation.

> Results are intentionally not committed: they depend on the host CPU, runtime, and load, so any
> table here would be misleading on another machine. Run the suite locally to get numbers for your
> environment.
