namespace Moongazing.OrionLedger.Tests;

using System.Diagnostics.Metrics;

using Moongazing.OrionLedger;
using Moongazing.OrionLedger.Diagnostics;
using Moongazing.OrionLedger.Keys;
using Moongazing.OrionLedger.Observers;
using Moongazing.OrionLedger.Storage;

using Xunit;

/// <summary>
/// Observer notification and OpenTelemetry instrumentation coverage. Observers are observability
/// only and must never break the lifecycle; the meter must emit one measurement per operation with
/// the correct status tag.
/// </summary>
public sealed class ApiKeyServiceObservabilityTests
{
    private static ApiKeyService CreateService(
        ApiKeyDiagnostics diagnostics,
        IApiKeyEventObserver? observer,
        InMemoryApiKeyStore store,
        Func<DateTimeOffset> now) =>
        new(store, new ApiKeyOptions(), diagnostics, observer, now, newId: () => Guid.NewGuid().ToString("N"));

    // ---- Observer notification -------------------------------------------------------------

    [Fact]
    public async Task The_observer_is_notified_on_issue()
    {
        var observer = new RecordingApiKeyEventObserver();
        using var diagnostics = new ApiKeyDiagnostics();
        var store = new InMemoryApiKeyStore();
        var service = CreateService(diagnostics, observer, store, () => DateTimeOffset.UnixEpoch);

        var issued = await service.IssueAsync("acme");

        Assert.Single(observer.Issued);
        Assert.Equal(issued.Record.Id, observer.Issued[0].Id);
    }

    [Fact]
    public async Task The_observer_is_notified_on_every_verification_including_failures()
    {
        var observer = new RecordingApiKeyEventObserver();
        using var diagnostics = new ApiKeyDiagnostics();
        var store = new InMemoryApiKeyStore();
        var service = CreateService(diagnostics, observer, store, () => DateTimeOffset.UnixEpoch);

        var issued = await service.IssueAsync("acme");
        await service.VerifyAsync(issued.Token);     // valid
        await service.VerifyAsync("garbage");        // malformed
        await service.VerifyAsync("ork_unknownxyz"); // not found

        Assert.Equal(3, observer.Verified.Count);
        Assert.Equal(ApiKeyStatus.Valid, observer.Verified[0].Status);
        Assert.Equal(ApiKeyStatus.Malformed, observer.Verified[1].Status);
        Assert.Equal(ApiKeyStatus.NotFound, observer.Verified[2].Status);
    }

    [Fact]
    public async Task The_observer_is_notified_only_on_an_effective_revoke()
    {
        var observer = new RecordingApiKeyEventObserver();
        using var diagnostics = new ApiKeyDiagnostics();
        var store = new InMemoryApiKeyStore();
        var service = CreateService(diagnostics, observer, store, () => DateTimeOffset.UnixEpoch);

        var issued = await service.IssueAsync("acme");
        await service.RevokeAsync(issued.Record.Id); // effective
        await service.RevokeAsync(issued.Record.Id); // already revoked, no-op
        await service.RevokeAsync("unknown");        // no such key

        Assert.Single(observer.Revoked);
        Assert.Equal(issued.Record.Id, observer.Revoked[0].Id);
    }

    [Fact]
    public async Task A_throwing_observer_never_breaks_the_lifecycle()
    {
        var observer = new RecordingApiKeyEventObserver(throwOnEveryCallback: true);
        using var diagnostics = new ApiKeyDiagnostics();
        var store = new InMemoryApiKeyStore();
        var service = CreateService(diagnostics, observer, store, () => DateTimeOffset.UnixEpoch);

        // None of these should surface the observer's InvalidOperationException.
        var issued = await service.IssueAsync("acme");
        var verification = await service.VerifyAsync(issued.Token);
        var revoked = await service.RevokeAsync(issued.Record.Id);

        Assert.True(verification.IsValid);
        Assert.True(revoked);
        // The faulting observer still recorded that it was invoked for each event.
        Assert.Single(observer.Issued);
        Assert.Single(observer.Verified);
        Assert.Single(observer.Revoked);
    }

    [Fact]
    public async Task A_null_observer_is_tolerated()
    {
        using var diagnostics = new ApiKeyDiagnostics();
        var store = new InMemoryApiKeyStore();
        var service = CreateService(diagnostics, observer: null, store, () => DateTimeOffset.UnixEpoch);

        var issued = await service.IssueAsync("acme");
        Assert.True((await service.VerifyAsync(issued.Token)).IsValid);
    }

    // ---- Diagnostics meter -----------------------------------------------------------------

    [Fact]
    public async Task Issuance_revocation_and_verification_emit_metered_measurements()
    {
        using var diagnostics = new ApiKeyDiagnostics();
        var store = new InMemoryApiKeyStore();
        var service = CreateService(diagnostics, observer: null, store, () => DateTimeOffset.UnixEpoch);

        long issued = 0;
        long revoked = 0;
        var verificationsByStatus = new Dictionary<string, long>(StringComparer.Ordinal);

        // Filter to this diagnostics instance's own meter, not by name: other tests in the run
        // share the meter name "Moongazing.OrionLedger" and would otherwise leak measurements here.
        var ourMeter = diagnostics.Issued.Meter;

        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (ReferenceEquals(instrument.Meter, ourMeter))
            {
                l.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, _) =>
        {
            switch (instrument.Name)
            {
                case "orionledger.keys.issued":
                    Interlocked.Add(ref issued, measurement);
                    break;
                case "orionledger.keys.revoked":
                    Interlocked.Add(ref revoked, measurement);
                    break;
                case "orionledger.verifications":
                    var status = "unknown";
                    foreach (var tag in tags)
                    {
                        if (tag.Key == "status" && tag.Value is string s)
                        {
                            status = s;
                        }
                    }

                    lock (verificationsByStatus)
                    {
                        verificationsByStatus[status] = verificationsByStatus.GetValueOrDefault(status) + measurement;
                    }

                    break;
            }
        });
        listener.Start();

        var key = await service.IssueAsync("acme", ["orders:read"]);
        await service.VerifyAsync(key.Token);                       // valid
        await service.VerifyAsync("garbage");                       // malformed
        await service.VerifyAsync("ork_unknownsecret");             // not_found
        await service.VerifyAsync(key.Token, "orders:write");       // missing_scope
        await service.RevokeAsync(key.Record.Id);
        await service.VerifyAsync(key.Token);                       // revoked

        listener.Dispose(); // flush

        Assert.Equal(1, issued);
        Assert.Equal(1, revoked);
        Assert.Equal(1, verificationsByStatus.GetValueOrDefault("valid"));
        Assert.Equal(1, verificationsByStatus.GetValueOrDefault("malformed"));
        Assert.Equal(1, verificationsByStatus.GetValueOrDefault("not_found"));
        Assert.Equal(1, verificationsByStatus.GetValueOrDefault("missing_scope"));
        Assert.Equal(1, verificationsByStatus.GetValueOrDefault("revoked"));
    }

    [Fact]
    public void The_diagnostics_meter_carries_the_published_name()
    {
        using var diagnostics = new ApiKeyDiagnostics();
        Assert.Equal("Moongazing.OrionLedger", ApiKeyDiagnostics.MeterName);
        Assert.NotNull(diagnostics.Issued);
        Assert.NotNull(diagnostics.Verifications);
        Assert.NotNull(diagnostics.Revoked);
    }

    // ---- Constructor guards & options validation -------------------------------------------

    [Fact]
    public void The_service_constructor_rejects_a_null_store()
    {
        using var diagnostics = new ApiKeyDiagnostics();
        Assert.Throws<ArgumentNullException>(() =>
            new ApiKeyService(null!, new ApiKeyOptions(), diagnostics));
    }

    [Fact]
    public void The_service_constructor_rejects_null_options()
    {
        using var diagnostics = new ApiKeyDiagnostics();
        Assert.Throws<ArgumentNullException>(() =>
            new ApiKeyService(new InMemoryApiKeyStore(), null!, diagnostics));
    }

    [Fact]
    public void The_service_constructor_rejects_null_diagnostics()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new ApiKeyService(new InMemoryApiKeyStore(), new ApiKeyOptions(), null!));
    }

    [Fact]
    public void The_service_constructor_validates_options()
    {
        using var diagnostics = new ApiKeyDiagnostics();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new ApiKeyService(new InMemoryApiKeyStore(), new ApiKeyOptions { SecretByteLength = 4 }, diagnostics));
    }
}
