using AskLucy.Application.SiteAnalysis.Providers;

namespace AskLucy.Infrastructure.SiteAnalysis.Providers;

/// <summary>
/// specs/088-environmental-context-specialist FR-017/FR-018 — the flood seam's first implementation.
/// It exists because a consumer now exists: a specialist that calls
/// <see cref="IFloodDataProvider"/> cannot be constructed while the interface is unregistered, and an
/// unresolvable <c>IAgentTool</c> breaks the whole agent tool catalog.
///
/// <para>
/// It deliberately carries no data. Its only behaviour is to report
/// <see cref="DataResolutionOutcomeType.Unavailable"/> with a reason, which is the honest statement
/// that no flood source is configured — never an estimate and never a failure. When a licensed
/// source is added it replaces this registration in
/// <c>AskLucy.Infrastructure.DependencyInjection</c>; this specialist's calling code does not change.
/// </para>
///
/// <para>
/// It performs no I/O, reads no credential, and never throws: unavailability is an outcome, not an
/// error (spec FR-035).
/// </para>
/// </summary>
public sealed class UnavailableFloodDataProvider : IFloodDataProvider
{
    internal const string Reason = "No flood data source is configured.";

    public Task<DataResolutionOutcome<FloodData>> ResolveAsync(
        double latitude, double longitude, CancellationToken cancellationToken = default) =>
        Task.FromResult(DataResolutionOutcome.Unavailable<FloodData>(Reason));
}
