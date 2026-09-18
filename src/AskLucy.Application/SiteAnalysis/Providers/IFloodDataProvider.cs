namespace AskLucy.Application.SiteAnalysis.Providers;

/// <summary>Flood modelling and hydrology for a site — not in OpenStreetMap (research.md D6/D11). No implementation ships with this release; see <see cref="DataResolutionOutcome{T}"/>.</summary>
public sealed record FloodData(string? FloodZoneClassification, double? AnnualExceedanceProbabilityPercent);

public interface IFloodDataProvider
{
    Task<DataResolutionOutcome<FloodData>> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
