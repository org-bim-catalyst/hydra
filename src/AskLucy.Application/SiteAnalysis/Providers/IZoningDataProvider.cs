namespace AskLucy.Application.SiteAnalysis.Providers;

/// <summary>Floor area ratio, setbacks, and height limits for a site — not in OpenStreetMap, the only geospatial source currently integrated (research.md D6/D11). No implementation ships with this release; see <see cref="DataResolutionOutcome{T}"/>.</summary>
public sealed record ZoningData(double? FloorAreaRatio, double? SetbackMeters, double? HeightLimitMeters);

public interface IZoningDataProvider
{
    Task<DataResolutionOutcome<ZoningData>> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
