using AskLucy.Application.SiteAnalysis.Providers;

namespace AskLucy.Infrastructure.SiteAnalysis.Providers;

/// <summary>
/// The zoning seam's first implementation, mirroring <see cref="UnavailableClimateDataProvider"/>
/// and <see cref="UnavailableFloodDataProvider"/>.
///
/// <para>
/// It deliberately carries no data. Its only behaviour is to report
/// <see cref="DataResolutionOutcomeType.Unavailable"/> with a reason, which is the honest statement
/// that no zoning source is configured. A licensed source replaces this registration; the site
/// character specialist's calling code does not change.
/// </para>
///
/// <para>
/// It performs no I/O, reads no credential, and never throws: unavailability is an outcome, not an
/// error (spec FR-035).
/// </para>
/// </summary>
public sealed class UnavailableZoningDataProvider : IZoningDataProvider
{
    internal const string Reason = "No zoning data source is configured.";

    public Task<DataResolutionOutcome<ZoningData>> ResolveAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default) =>
        Task.FromResult(DataResolutionOutcome.Unavailable<ZoningData>(Reason));
}
