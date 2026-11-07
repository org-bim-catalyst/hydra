namespace AskLucy.Application.Spatial.Query;

/// <summary>
/// specs/095 — one streaming batch delivered by <see cref="ISpatialQueryProvider.ExecuteAsync"/>.
/// </summary>
/// <param name="BatchIndex">Zero-based; monotonically increasing within one call.</param>
/// <param name="Features">1–<c>batchSize</c> features; may be empty only on the final batch.</param>
/// <param name="TotalCount">Running cumulative feature count across all batches so far.</param>
/// <param name="EstimatedTotal">Advisory provider hint at the eventual total; may be null.</param>
/// <param name="IsFinal">True on the last batch; always emitted exactly once.</param>
public sealed record QueryResultBatch(
    int BatchIndex,
    IReadOnlyList<QueryResultFeature> Features,
    int TotalCount,
    int? EstimatedTotal,
    bool IsFinal);
