# Contract: Spatial Query Provider

**Spec**: SPEC-095 | **Requirements**: FR-007, FR-011, FR-012, FR-015

The only route by which the `SpatialQuerySpecialist` retrieves geographic features matching a
`SpatialQueryDefinition`. Implemented in Infrastructure; consumed by Application.

---

## Interface

`AskLucy.Application.Spatial.Query.ISpatialQueryProvider`

```csharp
public interface ISpatialQueryProvider
{
    /// <summary>
    /// The dataset id(s) this provider handles.
    /// One provider may handle multiple datasets (e.g. an open-data adapter for parcels + buildings).
    /// </summary>
    IReadOnlyList<string> SupportedDatasetIds { get; }

    /// <summary>
    /// Streams matching features in batches. The caller controls cancellation via the token;
    /// the token deadline enforces the 30-second timeout (spec SC-001 / edge-case timeout rule).
    /// </summary>
    IAsyncEnumerable<QueryResultBatch> ExecuteAsync(
        SpatialQueryDefinition query,
        SpatialExecutionContext context,
        CancellationToken cancellationToken);
}
```

---

## `QueryResultBatch`

```csharp
public sealed record QueryResultBatch(
    int BatchIndex,
    IReadOnlyList<QueryResultFeature> Features,
    int TotalCount,
    int? EstimatedTotal,
    bool IsFinal);
```

| Field | Rules |
|---|---|
| `BatchIndex` | Zero-based; monotonically increasing within one `ExecuteAsync` call. |
| `Features` | 1–`query.BatchSize` features per batch. The final batch may contain fewer. |
| `TotalCount` | Running cumulative count of features delivered (sum of all batch sizes so far, including this batch). Not the eventual total. |
| `EstimatedTotal` | Optional provider hint at the eventual total. May be null or inaccurate; treated as advisory only. |
| `IsFinal` | `true` only on the last batch. The provider must always emit exactly one `IsFinal = true` batch, even if it is empty (zero features). |

---

## `QueryResultFeature`

```csharp
public sealed record QueryResultFeature(
    string FeatureId,
    string Name,
    FeatureGeometry Geometry,
    IReadOnlyDictionary<string, object?> Attributes);

public sealed record FeatureGeometry(
    FeatureGeometryKind Kind,
    IReadOnlyList<GeoPoint> Points);

public enum FeatureGeometryKind { Point, Polygon, LineString }
```

| Constraint | Rule |
|---|---|
| `FeatureId` | Non-empty; stable within the dataset for the life of the query session. Used in the audit log. |
| `Geometry.Points` | Point: exactly one element. Polygon: closed ring with ≥ 4 elements. LineString: ≥ 2 elements. |
| `Attributes` | Only fields declared in `DatasetEntry.attributeFields` are included. Values are pre-converted to the declared type. |

---

## Rules

| Rule | Requirement |
|---|---|
| A provider must never return `Empty` to signal a provider error | An unavailable data source → `OperationCanceledException` or a typed exception; the specialist catches it and returns `Failed`. `IsFinal = true` on an empty `Features` list signals a successful zero-result query. |
| Cancellation must be honoured | The `CancellationToken` passed to `ExecuteAsync` must be forwarded to every I/O call. When cancelled, the async enumerable must stop yielding and throw `OperationCanceledException`. |
| Partial coverage must be declared | If the provider could not cover the full study area or dataset, it emits a `QueryResultBatch` with `IsFinal = true` and a non-null `EstimatedTotal` (using the covered portion's count), and the specialist records a `Warnings` entry. |
| Attributes must not include undeclared fields | Only fields in `DatasetEntry.attributeFields` may be included. Extra fields from the upstream source must be dropped before the batch is returned. |
| Feature IDs must be unique within one `ExecuteAsync` call | Duplicate `FeatureId` values in different batches are a provider contract violation; the specialist deduplicates and logs a warning. |

---

## Registering a New Provider

1. Implement `ISpatialQueryProvider` with the appropriate `SupportedDatasetIds`.
2. Register in `src/AskLucy.Application/DependencyInjection.cs` as `ISpatialQueryProvider`.
3. Add the corresponding `DatasetEntry` records to `DatasetCatalog`.

No change to `SpatialQuerySpecialist`, `SpatialAnalysisCoordinator`, `SpatialResultNormalizer`,
or the viewer is required.
