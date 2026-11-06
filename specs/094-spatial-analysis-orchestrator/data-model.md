# Data Model: Spatial Request Bundle & Analysis Orchestrator

This feature adds **transient** Application models and viewer-session state. No database entity, table or
migration is introduced. All models live in `src/AskLucy.Application/Spatial/`.

## SpatialRequestBundle

The versioned interpretation of one user request. Produced by the coordinator; consumed only by specialists.

| Field | Type | Rules |
|---|---|---|
| `bundleVersion` | integer | Required. Currently `1`. Any other value is rejected with a user-visible explanation (FR-003). |
| `bundleId` | GUID (v7-compatible string) | Required. Identifies one interpreted request; used in logs and layer ownership. |
| `operations` | `SpatialOperation[]` | Required, at least one. |
| `preferences` | `SpatialPreferences` | Optional. Carries `layerMode` (`keep` \| `replace` \| `ask`) and any explicit user instruction. |
| `mapContext` | `MapViewportContext?` | Optional. Validated when present. |
| `requestedAtUtc` | ISO-8601 timestamp | Required, set by the coordinator. |

**Versioning rule**: adding a specialist adds a new `operationKind` and its parameter block. Existing field
meanings never change within a version. A breaking change increments `bundleVersion`; the coordinator supports
exactly the versions it declares, and rejects the rest rather than guessing.

## SpatialOperation

One unit of work in a bundle.

| Field | Type | Rules |
|---|---|---|
| `operationId` | string | Required, unique within the bundle. |
| `operationKind` | string | Required. Known values: `poi.discovery`, `proximity.analysis`, and future registered kinds. Unknown kinds fail validation. |
| `targets` | `FeatureSelector[]` | Required for result-producing operations; empty only for operations that resolve context (none in v1). |
| `parameters` | JSON object | Operation-specific. Validated by the owning specialist against the operation's schema. |
| `studyArea` | `StudyAreaReference?` | Required when the operation is area-based. Must resolve to an authorized, bounded area. |
| `dependsOn` | string[] | Optional. Must reference earlier `operationId`s in the same bundle; cycles are rejected. |

### FeatureSelector

- `category`: normalized category key (e.g. `school`) or the user's category phrase before catalog resolution.
- `geometry`: optional point/line/polygon when the user supplied coordinates or the coordinator resolved one.
- `dataSource`: optional source selector, constrained to configured/available sources.
- At least one of `category` or `geometry` is required.

### Distance/Area parameters (v1)

`proximity.analysis` parameters follow spec 093's data model: source/target selectors, one or more
`DistanceThreshold` values in metres after normalization, and the study area. Units are `m` or `km` on input and
preserved for display.

## MapViewportContext

Captured by the client from the live map at send time; never trusted.

| Field | Type | Rules |
|---|---|---|
| `centerLatitude` | double | Finite, −90…90. |
| `centerLongitude` | double | Finite, −180…180. |
| `zoom` | double | Finite, within the viewer's supported range. |
| `southWestLatitude` / `southWestLongitude` | double | Finite; south ≤ north, west ≤ east after normalization. |
| `northEastLatitude` / `northEastLongitude` | double | Finite; bounds must form a non-degenerate box. |

The active boundary is **not** part of this object: the server already holds it on `TurnContext.ActiveBoundary`.
The coordinator combines both in its `SpatialExecutionContext`.

## StudyAreaReference

- `kind`: `namedArea` | `mapViewport` | `activeBoundary` | `explicitGeometry`.
- `namedArea`: user-stated place name, resolved through `ILocationResolutionService`.
- `geometry`: polygon when supplied by the boundary system or map context.
- Resolution order: explicit user statement → active boundary → map viewport → ask (FR-016).

## SpatialAnalysisResult

The coordinator's normalized output.

| Field | Type | Rules |
|---|---|---|
| `analysisId` | string | Required; stable for the request. |
| `bundleId` | string | Required. |
| `status` | enum `Complete` \| `Partial` \| `Empty` \| `Failed` | Required. `Empty` is a successful zero-result analysis; `Failed` means no operation produced a result. |
| `resolvedRequest` | summary | Operation kinds, categories/geometries, thresholds/units and the study area actually used. |
| `layers` | `SpatialLayer[]` | Zero or more. Empty only for `Empty`/`Failed` or a clarification outcome. |
| `resultCount` | integer | Total features across layers after de-duplication. |
| `coverage` | `DataSourceCoverage[]` | Source id, display name, `Complete`/`Partial`/`Unavailable`, user-safe explanation. |
| `warnings` | string[] | User-readable limitations, failed operations, clipping notices. |
| `operations` | `SpatialOperationOutcome[]` | Per-operation status and provenance; required for `Partial`. |

### SpatialOperationOutcome

- `operationId`, `operationKind`, `status` (`Succeeded` | `Empty` | `Failed`).
- `specialistName`: the concrete specialist type's stable name.
- `failureReason`: diagnostic detail (never user-facing copy; the coordinator composes the user message).
- `coverage`: source/coverage entries contributed by this operation.

## SpatialLayer

A renderable, user-controlled layer. Emitted to the client; session-scoped.

| Field | Type | Rules |
|---|---|---|
| `layerId` | string | Required, unique within the analysis. |
| `analysisId` | string | Required. Ownership for clear-analysis. |
| `sourceOperationId` | string | Required. Provenance. |
| `name` | string | Required, user-visible, ≤ 80 characters. |
| `legendLabel` | string | Required, synchronized with `name`. |
| `kind` | enum `markers` \| `buffer` \| `area` \| `band` | Required. |
| `markers` | `SpatialMarker[]` | Required for `markers`. Points WGS-84; coordinates finite and in range. |
| `ring` | `GeoPoint[]` | Required for `buffer`, `area`, `band`. Closed, ≥ 3 distinct vertices, WGS-84. |
| `style` | `SpatialLayerStyle` | Required. Colour, shape/opacity; derived from category or band, never from model text. |
| `visible` | boolean | Defaults to true on creation. |
| `attributes` | key/value map | Optional; allowlisted feature attributes only. No external URLs, file paths, or untrusted HTML. |

### SpatialLayerStyle

`color` (hex), `markerShape` (for `markers`), `opacity` (0–1), `strokeWidth` (for `buffer`/`area`). Server-assigned
from the category catalog or band definition so two layers never share an identical style unintentionally.

## SpatialLayerState (session)

In-process, per chat; the generalization of `PoiLayerStateStore`.

| Field | Type | Rules |
|---|---|---|
| `chatId` | GUID | Key. |
| `layers` | `SpatialLayerStateEntry[]` | Ordered by insertion. Entry: `analysisId`, `layerId`, `name`, `kind`, `legendLabel`. |
| `pending` | `PendingSpatialQuestion?` | The outstanding keep/replace or which-area question. |
| `pending.expiresAtUtc` | timestamp | 10 minutes after creation, matching the current POI store. |

**State transitions**

```text
(none) --interpret--> Interpreted --validate--> Rejected (user-visible, no execution)
Interpreted --dispatch--> Running --merge--> Complete | Partial | Empty | Failed
Complete/Partial/Empty --deliver--> LayersOnMap --user action--> Hidden | Removed | Cleared
Running --user clears--> Cleared (in-flight result still ends with exactly one outcome)
```

- A request ends in exactly one outcome (`Complete`, `Partial`, `Empty`, `Failed`, or a clarification question).
- `Hidden` and `Removed` affect only viewer state; conversation messages are untouched.
- Clearing all layers removes the chat's entry; a pending question is preserved until it expires or is answered.

## Validation invariants

- Bundle version must be supported; unknown versions are rejected.
- At least one operation; unique operation ids; `dependsOn` references exist and are acyclic.
- Every operation kind resolves to a registered specialist; the rest are reported as not performed, never
  silently dropped.
- Area-based operations require a study area; map-viewport areas must be bounded and within provider limits.
- Coordinates: finite, WGS-84; polygons closed with ≥ 3 distinct vertices.
- Thresholds: positive, finite, supported units; bands ordered, non-overlapping, positive width (spec 093).
- Layer attributes: allowlist only; no URLs, paths, or markup.
- A specialist failure produces `Failed` for that operation, never an empty result.
- Cancellation is propagated; a cancelled operation is `Failed` with a cancellation reason and is not delivered
  as a layer.
