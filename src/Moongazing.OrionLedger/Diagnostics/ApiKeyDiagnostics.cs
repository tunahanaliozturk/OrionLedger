namespace Moongazing.OrionLedger.Diagnostics;

using System.Diagnostics.Metrics;

/// <summary>
/// OpenTelemetry instrumentation for the key lifecycle. Exposes a <see cref="Meter"/> named
/// <c>Moongazing.OrionLedger</c> with issuance, verification, and revocation counters. Registered
/// as a singleton; dispose it to release the meter.
/// </summary>
public sealed class ApiKeyDiagnostics : IDisposable
{
    /// <summary>The meter name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionLedger";

    private readonly Meter meter;

    /// <summary>Create the meter and its instruments.</summary>
    public ApiKeyDiagnostics()
    {
        meter = new Meter(MeterName, "0.1.0");

        Issued = meter.CreateCounter<long>(
            "orionledger.keys.issued",
            unit: "{key}",
            description: "API keys issued.");

        Verifications = meter.CreateCounter<long>(
            "orionledger.verifications",
            unit: "{verification}",
            description: "Key verifications, tagged status "
                + "(valid/malformed/not_found/expired/revoked/missing_scope).");

        Revoked = meter.CreateCounter<long>(
            "orionledger.keys.revoked",
            unit: "{key}",
            description: "API keys revoked.");
    }

    /// <summary>Counts issued keys.</summary>
    public Counter<long> Issued { get; }

    /// <summary>Counts verifications by status.</summary>
    public Counter<long> Verifications { get; }

    /// <summary>Counts revocations.</summary>
    public Counter<long> Revoked { get; }

    /// <summary>Record a verification outcome.</summary>
    /// <param name="status">The status tag value.</param>
    public void RecordVerification(string status) =>
        Verifications.Add(1, new KeyValuePair<string, object?>("status", status));

    /// <inheritdoc />
    public void Dispose() => meter.Dispose();
}
