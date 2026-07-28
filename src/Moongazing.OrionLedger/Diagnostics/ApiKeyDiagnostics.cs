namespace Moongazing.OrionLedger.Diagnostics;

using System.Diagnostics.Metrics;

using Moongazing.Orion.Abstractions.Diagnostics;

/// <summary>
/// OpenTelemetry instrumentation for the key lifecycle. Built on the Orion family's
/// <see cref="OrionInstrumentation"/> spine, so it shares the family's naming and static-tag
/// conventions: a <see cref="Meter"/> named <c>Moongazing.OrionLedger</c> (subscribe by that name)
/// exposing issuance, verification, revocation, and rotation counters under the
/// <c>orion.ledger.*</c> names. Multi-tenant / multi-region labels configured through
/// <see cref="OrionInstrumentation.SetStaticTags"/> are stamped onto every measurement recorded
/// through the <c>Record*</c> methods. Registered as a singleton; dispose it to release the meter.
/// </summary>
public sealed class ApiKeyDiagnostics : OrionInstrumentation
{
    /// <summary>The meter name OpenTelemetry consumers subscribe to.</summary>
    public const string MeterName = "Moongazing.OrionLedger";

    /// <summary>Create the meter and its instruments.</summary>
    public ApiKeyDiagnostics()
        : base(OrionTelemetry.ScopeName("OrionLedger"), MeterVersion.Value)
    {
        Issued = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("ledger", "keys.issued"),
            unit: "{key}",
            description: "API keys issued.");

        Verifications = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("ledger", "verifications"),
            unit: "{verification}",
            description: "Key verifications, tagged status "
                + "(valid/malformed/not_found/expired/revoked/retired/missing_scope).");

        Revoked = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("ledger", "keys.revoked"),
            unit: "{key}",
            description: "API keys revoked, including keys swept by bulk revocation.");

        Rotated = Meter.CreateCounter<long>(
            OrionTelemetry.MetricName("ledger", "keys.rotated"),
            unit: "{key}",
            description: "API keys rotated (a successor key was issued for an existing key).");
    }

    /// <summary>Counts issued keys.</summary>
    public Counter<long> Issued { get; }

    /// <summary>Counts verifications by status.</summary>
    public Counter<long> Verifications { get; }

    /// <summary>Counts revocations, including keys swept by bulk revocation.</summary>
    public Counter<long> Revoked { get; }

    /// <summary>Counts rotations (each rotation issues one successor key).</summary>
    public Counter<long> Rotated { get; }

    /// <summary>Record one issued key.</summary>
    public void RecordIssued() => Issued.Add(1, StaticTags);

    /// <summary>Record one revoked key.</summary>
    public void RecordRevoked() => Revoked.Add(1, StaticTags);

    /// <summary>Record one rotation.</summary>
    public void RecordRotated() => Rotated.Add(1, StaticTags);

    /// <summary>Record a verification outcome.</summary>
    /// <param name="status">The status tag value.</param>
    public void RecordVerification(string status) =>
        Verifications.Add(1, Tag(new KeyValuePair<string, object?>("status", status)));
}
