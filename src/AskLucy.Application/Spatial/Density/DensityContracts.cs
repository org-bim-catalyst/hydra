using AskLucy.Domain.SiteBoundaries;

namespace AskLucy.Application.Spatial.Density;

public sealed record DensityDatasetEntry(
    string Id,
    string DisplayName,
    IReadOnlyList<DensityMethod> SupportedMethods,
    string CanonicalUnit,
    string ProviderId,
    string? CategoryKey = null);

public interface IDensityDatasetCatalog
{
    bool TryGet(string datasetId, out DensityDatasetEntry entry);
    string DescribeAvailable();
}

public sealed class DensityDatasetCatalog(IEnumerable<DensityDatasetEntry> entries) : IDensityDatasetCatalog
{
    private readonly IReadOnlyDictionary<string, DensityDatasetEntry> _entries = entries
        .ToDictionary(entry => entry.Id, StringComparer.OrdinalIgnoreCase);
    public bool TryGet(string datasetId, out DensityDatasetEntry entry) => _entries.TryGetValue(datasetId, out entry!);
    public string DescribeAvailable() => string.Join(", ", _entries.Values.Select(x => x.DisplayName));
}

public sealed record DensityAnalysisDefinition(
    string AnalysisId, DensityDatasetEntry Dataset, ResolvedStudyArea StudyArea,
    DensityIntent Intent, DensityMethod Method, double? SearchRadiusMeters,
    double? CellSizeMeters, DensityClassificationMethod Classification);

public sealed record DensityCell(string Id, IReadOnlyList<GeoPoint> Ring, double Value,
    string Unit, int? ClassificationIndex = null, string? ClassificationLabel = null, int SourceCount = 0);
public sealed record DensityClassificationResult(DensityClassificationMethod Method,
    IReadOnlyList<double> Breaks, IReadOnlyList<string> Labels);
public sealed record DensityProviderResult(IReadOnlyList<DensityCell> Cells,
    DensityClassificationResult Classification, IReadOnlyList<DataSourceCoverage> Coverage,
    IReadOnlyList<string>? Warnings = null);

public interface IDensityAnalysisProvider
{
    IReadOnlyList<string> SupportedDatasetIds { get; }
    Task<DensityProviderResult> CalculateAsync(DensityAnalysisDefinition definition,
        SpatialExecutionContext context, CancellationToken cancellationToken);
}
