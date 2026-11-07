# Data Model: Spatial Query Analysis (SPEC-095)

This feature adds **transient** Application models and viewer-session state only. No database entity,
table, or migration is introduced. All backend models live in
`src/AskLucy.Application/Spatial/Query/`.

---

## SpatialQueryParameters *(extends `SpatialOperationParameters`)*

Carried inside a `SpatialOperation` with `operationKind = "spatial.query"`. All fields are optional
in the bundle wire shape; FluentValidation enforces the semantic rules below.

| Field | Type | Rules |
|---|---|---|
| `datasetId` | string | Required. Must match a registered `DatasetEntry.Id`. Unknown id → validation error with available-datasets list. |
| `attributePredicates` | `AttributePredicate[]` | Optional; at least one predicate required when `spatialPredicates` is empty. |
| `attributeCombinator` | `"and"` \| `"or"` | Optional; defaults to `"and"`. Governs how attribute predicates are combined within their group. |
| `spatialPredicates` | `SpatialPredicate[]` | Optional; at least one predicate required when `attributePredicates` is empty. |
| `spatialCombinator` | `"and"` \| `"or"` | Optional; defaults to `"and"`. Governs how spatial predicates are combined within their group. |

> Validation rule (clarification Q4): `attributeCombinator` and `spatialCombinator` are independent.
> A mixed-type OR (an attribute predicate OR'd with a spatial predicate) is never expressed in this
> model; the two groups are always AND'd together at query-definition level.

---

## AttributePredicate

One filter condition applied to a dataset attribute field.

| Field | Type | Rules |
|---|---|---|
| `field` | string | Required. Must match a `AttributeField.Name` on the target `DatasetEntry`. |
| `operator` | enum | Required. One of: `Gt`, `Gte`, `Lt`, `Lte`, `Eq`, `NotEq`, `Between`, `Contains`, `StartsWith`, `EndsWith`, `InYearRange`. |
| `value` | string \| number | Required. Parsed and coerced to the field's declared type. |
| `valueTo` | string \| number | Required only for `Between` and `InYearRange`. |
| `unit` | string | Optional. When present and the field declares a unit, the value is converted to the field's canonical unit before comparison. When absent and the field's unit is unambiguous, the canonical unit is used and stated in Lucy's response (spec FR-002 / clarification Q4-US4). |

### Supported Operators by Field Type

| Field Type | Supported Operators |
|---|---|
| `numeric` | `Gt`, `Gte`, `Lt`, `Lte`, `Eq`, `NotEq`, `Between` |
| `string` | `Eq`, `NotEq`, `Contains`, `StartsWith`, `EndsWith` |
| `year` / `date` | `Eq`, `NotEq`, `Gt`, `Gte`, `Lt`, `Lte`, `Between`, `InYearRange` |

---

## SpatialPredicate

One spatial relationship condition applied to the target dataset.

| Field | Type | Rules |
|---|---|---|
| `relationship` | enum | Required. One of: `Within`, `Outside`, `Intersecting`, `Containing`, `Near`. |
| `referenceKind` | `"namedFeature"` \| `"activeLayer"` \| `"studyArea"` \| `"explicitGeometry"` | Required. How the reference geometry is provided. |
| `referenceName` | string | Required when `referenceKind = "namedFeature"`. Resolved via `ILocationResolutionService`. |
| `referenceLayerId` | string | Required when `referenceKind = "activeLayer"`. Must match an active `SpatialLayer.layerId`. |
| `referenceGeometry` | `GeoPoint[]` | Required when `referenceKind = "explicitGeometry"`. |
| `distanceMeters` | double | Required only for `Near`. Must be positive and finite. The user's distance and unit are converted to metres before this field is set. |

### Spatial Relationship Semantics

| Relationship | Meaning |
|---|---|
| `Within` | Feature geometry is fully enclosed by the reference geometry. |
| `Outside` | Feature geometry has no overlap with the reference geometry (disjoint). |
| `Intersecting` | Feature geometry shares at least one point with the reference geometry (includes boundary touch). |
| `Containing` | Feature geometry fully encloses the reference geometry. |
| `Near` | Feature geometry is within `distanceMeters` of the reference geometry's nearest point. |

---

## SpatialQueryDefinition

The validated, typed representation of one query operation. Produced by the specialist from the
bundle operation; consumed by `ISpatialQueryProvider`.

| Field | Type | Rules |
|---|---|---|
| `queryId` | string | Required; equals the bundle operation's `operationId`. |
| `dataset` | `DatasetEntry` | Required; resolved from `IDatasetCatalog`. |
| `attributeGroup` | `PredicateGroup<AttributePredicate>?` | Present when at least one attribute predicate is defined. |
| `spatialGroup` | `PredicateGroup<SpatialPredicate>?` | Present when at least one spatial predicate is defined. |
| `studyArea` | `ResolvedStudyArea?` | The resolved study area from the bundle operation. |
| `batchSize` | int | Configured; default 200 features per batch. |
| `timeoutAt` | `DateTime` (UTC) | Set by the specialist to `now + 30 s`; passed to the provider as a `CancellationToken` deadline. |

### PredicateGroup\<T\>

A typed group of same-kind predicates with their combinator.

| Field | Type | Rules |
|---|---|---|
| `predicates` | `T[]` | At least one. |
| `combinator` | `And` \| `Or` | Defaults to `And`. |

---

## DatasetEntry

Describes one queryable geographic dataset.

| Field | Type | Rules |
|---|---|---|
| `id` | string | Unique; lower-kebab-case (e.g. `land-parcels`). |
| `displayName` | string | User-facing name (e.g. "Land Parcels"). |
| `attributeFields` | `AttributeField[]` | At least one. Each field has: `name`, `displayName`, `type` (`numeric`\|`string`\|`year`\|`date`), `unit?`, `exampleValue`. |
| `supportedRelationships` | `SpatialRelationship[]` | Subset of `{Within, Outside, Intersecting, Containing, Near}` the provider supports. |
| `providerId` | string | Identifies which `ISpatialQueryProvider` handles this dataset. |

### Initial Dataset Catalog Entries

| Id | Display Name | Key Attribute Fields | Supported Relationships |
|---|---|---|---|
| `land-parcels` | Land Parcels | `area` (numeric, m²), `land-use` (string), `zone` (string) | Within, Outside, Intersecting, Containing |
| `buildings` | Buildings | `height` (numeric, m), `floors` (numeric), `year-built` (year), `use` (string) | Within, Outside, Intersecting, Containing, Near |
| `roads` | Roads | `name` (string), `classification` (string), `surface` (string) | Within, Intersecting |
| `development-areas` | Development Areas | `name` (string), `status` (string), `area` (numeric, m²) | Within, Outside, Intersecting, Containing |

---

## QueryResultFeature

One geographic feature returned by an `ISpatialQueryProvider`.

| Field | Type | Rules |
|---|---|---|
| `featureId` | string | Required; stable identifier within the dataset (used in audit log). |
| `name` | string | Required; display name. Empty string is valid but not preferred. |
| `geometry` | `FeatureGeometry` | Required. See below. |
| `attributes` | `Dictionary<string, object?>` | Required; keyed by `AttributeField.name`. Values are pre-converted to the field's canonical type. Only fields in the `DatasetEntry.attributeFields` list are included. |

### FeatureGeometry

| Field | Type | Rules |
|---|---|---|
| `kind` | `"point"` \| `"polygon"` \| `"linestring"` | Required. |
| `points` | `GeoPoint[]` | Required. For `point`: one element. For `polygon`: closed ring (first = last), ≥ 4 points. For `linestring`: ≥ 2 points. |

---

## QueryResultBatch

One streaming batch delivered by `ISpatialQueryProvider.ExecuteAsync`.

| Field | Type | Rules |
|---|---|---|
| `batchIndex` | int | Zero-based; monotonically increasing. |
| `features` | `QueryResultFeature[]` | 1–`batchSize` features. |
| `totalCount` | int | Running total of features delivered so far; not the eventual total (which is unknown during streaming). |
| `estimatedTotal` | int? | Optional hint from the provider about the eventual total; may be null or inaccurate. |
| `isFinal` | bool | True on the last batch. If the timeout fires before `isFinal = true`, the specialist emits the last accumulated batch with `isFinal = true` and `Warnings` explaining the timeout. |

---

## SpatialQueryAuditEntry

One immutable structured-log record written per query execution (clarification Q5).
Written via `Serilog.ILogger` as a structured property; never stored in the database.

| Field | Type | Notes |
|---|---|---|
| `auditEventType` | `"SpatialQueryExecuted"` | Constant; enables log filtering. |
| `bundleId` | string | From the bundle. |
| `operationId` | string | From the operation. |
| `userId` | string | From `SpatialExecutionContext.UserId`. |
| `datasetId` | string | From `DatasetEntry.Id`. |
| `queryDefinition` | `SpatialQueryDefinition` | Serialized; full predicate set as the user expressed it after resolution. |
| `outcome` | `"Success"` \| `"Empty"` \| `"Error"` \| `"Timeout"` | Maps from `SpatialOperationStatus`. |
| `durationMs` | long | Wall-clock milliseconds from specialist entry to outcome. |
| `resultCount` | int | Total features returned across all batches. |
| `returnedFeatureIds` | `string[]` | All `QueryResultFeature.featureId` values returned. Serialized as JSON array. |
| `failureReason` | string? | Diagnostic detail; never shown verbatim to the user. |

---

## Frontend Types (TypeScript)

All new types extend the existing `spatialLayersStore.ts` without breaking the current shape.

```typescript
// Extends SpatialLayerKind union
export type SpatialLayerKind = 'markers' | 'buffer' | 'area' | 'band' | 'feature-query'

// Feature attribute map — keyed by field name, value is display-ready string
export type FeatureAttributes = Record<string, string | number | null>

// Extended SpatialMarkerPoint with optional attributes
export interface SpatialMarkerPoint {
  id: string
  name: string
  latitude: number
  longitude: number
  attributes?: FeatureAttributes   // NEW — optional; present on feature-query layers
}

// Extended SpatialLayerPayload with batch streaming fields
export interface SpatialLayerPayload {
  // ... existing fields unchanged ...
  batchIndex?: number | null       // NEW — which batch this payload represents
  totalCount?: number | null       // NEW — running feature count across all batches so far
  estimatedTotal?: number | null   // NEW — provider hint; may be absent
  isFinal?: boolean | null         // NEW — true when streaming is complete for this layer
}
```

### Store extension: batch accumulation

`spatialLayersStore.ts` gains one new action:

```typescript
/** Merges an incoming batch into an existing layer, or creates a new layer for batchIndex === 0. */
applyBatch: (
  chatId: string,
  analysisId: string,
  layerId: string,
  batch: SpatialLayerPayload,
  mode: 'keep' | 'replace',
) => void
```

Existing actions (`toggleLayer`, `removeLayer`, `clearAnalysis`, `clearLayers`) are unchanged.
