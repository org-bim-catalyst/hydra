using AskLucy.Application.SiteAnalysis.Providers;

namespace AskLucy.Infrastructure.SiteAnalysis.Providers;

/// <summary>
/// specs/088-environmental-context-specialist FR-017/FR-018 — the climate seam's first
/// implementation, mirroring <see cref="UnavailableFloodDataProvider"/>.
///
/// <para>
/// It deliberately carries no data. Its only behaviour is to report
/// <see cref="DataResolutionOutcomeType.Unavailable"/> with a reason, which is the honest statement
/// that no climate source is configured. A licensed source replaces this registration; this
/// specialist's calling code does not change.
/// </para>
///
/// <para>
/// It performs no I/O, reads no credential, and never throws: unavailability is an outcome, not an
/// error (spec FR-035).
/// </para>
/// </summary>
public sealed class UnavailableClimateDataProvider : IClimateDataProvider
{
    internal const string Reason = "No climate data source is configured.";

    public Task<DataResolutionOutcome<ClimateData>> ResolveAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default) =>
        Task.FromResult(DataResolutionOutcome.Unavailable<ClimateData>(Reason));
}
