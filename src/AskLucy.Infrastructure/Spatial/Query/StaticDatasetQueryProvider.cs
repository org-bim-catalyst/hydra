using System.Runtime.CompilerServices;
using AskLucy.Application.Spatial;
using AskLucy.Application.Spatial.Query;

namespace AskLucy.Infrastructure.Spatial.Query;

/// <summary>
/// specs/095 — placeholder <see cref="ISpatialQueryProvider"/> for datasets that do not yet
/// have a live data source. Immediately emits one empty final batch so the specialist can
/// complete the audit log and return a clean "no results" outcome (T018).
/// </summary>
/// <remarks>
/// Replace by registering a real provider for the same <see cref="SupportedDatasetIds"/> in DI.
/// The specialist always selects the first matching provider, so a real provider registered
/// first takes precedence over this stub.
/// </remarks>
public sealed class StaticDatasetQueryProvider : ISpatialQueryProvider
{
    /// <inheritdoc/>
    public IReadOnlyList<string> SupportedDatasetIds { get; } = [];

#pragma warning disable CS1998 // Async method lacks 'await' — intentional: fast-path stub
    /// <inheritdoc/>
    public async IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
        SpatialQueryDefinition query,
        SpatialExecutionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        yield return new QueryResultBatch(
            BatchIndex: 0,
            Features: [],
            TotalCount: 0,
            EstimatedTotal: 0,
            IsFinal: true);
    }
#pragma warning restore CS1998
}
