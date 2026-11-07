using AskLucy.Application.Spatial;

namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — the only route by which <c>SpatialQuerySpecialist</c> retrieves features.
/// Implemented in Infrastructure; consumed by Application (Clean Architecture §2 I).
/// </summary>
public interface ISpatialQueryProvider
{
    /// <summary>Dataset ids this provider can serve. One provider may serve multiple datasets.</summary>
    IReadOnlyList<string> SupportedDatasetIds { get; }

    /// <summary>
    /// Streams matching features in batches. The <paramref name="cancellationToken"/> deadline
    /// enforces the 30-second timeout (spec SC-001 / edge-case timeout rule).
    /// The provider MUST always emit exactly one batch with <c>IsFinal = true</c>.
    /// </summary>
    IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
        SpatialQueryDefinition query,
        SpatialExecutionContext context,
        CancellationToken cancellationToken);
}
