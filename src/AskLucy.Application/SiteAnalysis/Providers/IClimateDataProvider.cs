namespace AskLucy.Application.SiteAnalysis.Providers;

/// <summary>Wind and climate conditions for a site — not in OpenStreetMap (research.md D6/D11). No implementation ships with this release; see <see cref="DataResolutionOutcome{T}"/>.</summary>
public sealed record ClimateData(double? PrevailingWindDirectionDegrees, double? AverageWindSpeedMetersPerSecond);

public interface IClimateDataProvider
{
    Task<DataResolutionOutcome<ClimateData>> ResolveAsync(double latitude, double longitude, CancellationToken cancellationToken = default);
}
