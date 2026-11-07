# Research: Spatial Query Analysis (SPEC-095)

**Date**: 2026-11-06
**Prerequisites**: spec 094 (Spatial Analysis Orchestrator), spec 093 (Proximity Analysis),
spec 092 (Dataset Sources), spec 090 (POI Discovery), spec 057 (Site Analysis Agent).

All decisions below were verified against the live codebase.

---

## D1 — Register as a new `ISpatialSpecialist`; zero changes to the coordinator

**Decision**: `SpatialQuerySpecialist` implements `ISpatialSpecialist` with
`OperationKind = "spatial.query"`. It is registered in `DependencyInjection.cs` and declared in
`SpatialOperationKinds.Supported`. No other file in the coordinator path changes.

**Rationale — verified against existing code**:

- `SpatialSpecialistRegistry` builds from `IEnumerable<ISpatialSpecialist>` registered with DI
  (`src/AskLucy.Application/Spatial/SpatialSpecialistRegistry.cs`). Adding a DI registration
  is the complete integration step.
- `SpatialOperationKinds.Supported` is the data declaration the coordinator uses for Tier-1
  interpretation wording. Adding a `SupportedSpatialOperation` record is the only edit needed
  to teach the coordinator that `spatial.query` exists as a choosable kind.
- `SpatialAnalysisCoordinator` dispatches by `operationKind → specialist.ExecuteAsync()`; it
  does not contain a switch on kinds. Zero coordinator changes required.
- `SpatialResultNormalizer` maps `SpatialSpecialistOutcome.Layers` to `SpatialLayer[]`; it
  only needs to know about a new `SpatialLayerKind` value to pass the layer through — which
  is a one-line enum extension (see D3 below).

**Alternatives considered**:

- *Extend `ProximityAnalysisSpecialist` to handle attribute queries* — rejected: violates SRP
  and conflates two distinct operations; proximity is distance-based, query is predicate-based.
- *New `IConversationCapability` parallel to the spatial coordinator* — rejected: creates a
  second spatial front door, explicitly forbidden by FR-014 and spec 094's design decision D2.

---

## D2 — `SpatialQueryDefinition` is the Application-layer typed query; `ISpatialQueryProvider` executes it

**Decision**: The specialist produces a `SpatialQueryDefinition` from the interpreted bundle operation.
An `ISpatialQueryProvider` (Application-owned interface) receives the definition and streams
`IAsyncEnumerable<QueryResultBatch>` of `QueryResultFeature` records back to the specialist.

**Rationale**:

- Keeping the query definition in Application preserves the dependency rule: Infrastructure adapters
  (`OverpassSpatialQueryProvider`, `StaticDatasetQueryProvider`) depend inward on the definition;
  Application never references a concrete provider.
- `IAsyncEnumerable<QueryResultBatch>` is the natural .NET primitive for streaming without
  introducing a new queue or push mechanism. The specialist iterates the stream, enforces the
  30-second timeout via `CancellationToken`, and accumulates batches into `SpatialLayerDraft`
  progressively.
- The coordinator already waits for `specialist.ExecuteAsync()` to return a complete
  `SpatialSpecialistOutcome` before emitting the trailing event. For streaming delivery to the
  client, the specialist internally collects all batches (up to timeout) and returns them as a
  single outcome with layers. The incremental batch delivery to the *frontend* is handled by
  the coordinator emitting multiple `__SPATIAL_LAYERS__` events (one per batch) with a
  `batchIndex` / `isFinal` envelope — a small extension to the existing trailing-event writer
  that the specialist triggers by yielding intermediate `SpatialLayerDraft` checkpoints.

**Alternatives considered**:

- *Single-shot provider returning all features* — rejected: blocks the first render until all
  features are retrieved; violates the 3-second first-batch SC-001 target for large datasets.
- *SignalR push from Infrastructure* — rejected: introduces a new hub dependency; violates the
  in-turn execution constraint from spec 094 D1.

---

## D3 — New `SpatialLayerKind.FeatureQuery`; existing viewer extended minimally

**Decision**: Add `FeatureQuery` to the `SpatialLayerKind` enum (backend) and `'feature-query'`
to the `SpatialLayerKind` union (frontend). `SpatialLayersOverlay` renders `feature-query` layers
as filled-polygon or point highlights using the layer's `ring` / `markers` geometry fields already
present on `SpatialLayerPayload`. `SpatialLayerLegend` shows a result count badge and a streaming
progress indicator when `isFinal` is false.

**Rationale**:

- The existing `SpatialLayerPayload` shape already carries `ring` (polygon geometry) and `markers`
  (point geometry). Feature-query results map cleanly onto these fields — a parcel is a ring; a
  building footprint is a ring; a road segment is a ring or markers. No new geometry field needed.
- `SpatialLayersStore` already holds `visible`, `removeLayer`, `clearLayers`, and `toggleLayer`;
  the only extension is batch accumulation (`batchIndex`, `totalCount`, `isFinal`) and a
  per-feature `attributes` map on `SpatialMarkerPoint`/ring points. The store shape change is
  backward-compatible (new optional fields).
- The client ignores unknown `kind` values (`isRenderable` returns false for unknown kinds);
  adding `'feature-query'` to the type union and the `validKinds` array is the complete
  frontend integration.

**Alternatives considered**:

- *Reuse `'area'` kind for polygon results* — rejected: loses semantic distinction; `area` is
  reserved for buffer zones. Using it for query results would confuse legend labels and legend
  icons.
- *New dedicated viewer extension with its own store* — rejected: unnecessary duplication;
  the existing `spatialLayersStore` already owns the layer lifecycle the spec requires.

---

## D4 — AND across predicate types; OR within predicate type (from clarification Q4)

**Decision**: The `SpatialQueryDefinition` carries two independent `PredicateGroup` fields:
`attributePredicates` and `spatialPredicates`. Each group has its own `Combinator` (`And` / `Or`).
The two groups are always combined with AND. A `SpatialQueryDefinition` with an `Or` combinator
spanning both groups is a validation error; the specialist returns `Failed` with a user-readable
explanation and offers to split into two queries.

**Rationale**:

- Keeps the query definition simple and unambiguous: a provider implementing `ISpatialQueryProvider`
  only needs to handle within-type boolean logic, not arbitrary predicate trees.
- Aligns with how every major GIS query tool (ArcGIS Definition Expressions, QGIS Filter, OGC CQL)
  handles the common case; mixed-type OR is extremely rare and always decomposable into two queries.
- Two-query alternative is surfaced explicitly by Lucy (spec FR-004 / clarification Q4), so the
  user is never left without a path forward.

---

## D5 — Audit log via Serilog structured log; no new table (from clarification Q5)

**Decision**: `SpatialQueryAuditLogger` writes one structured log entry per executed query using
`Serilog.ILogger`. The entry captures: `UserId` (from `SpatialExecutionContext`), `BundleId`,
`OperationId`, `DatasetId`, the full serialized `SpatialQueryDefinition`, `Outcome`
(`Success`/`Empty`/`Error`/`Timeout`), `DurationMs`, `ResultCount`, and `ReturnedFeatureIds[]`.
Retention is governed by the existing logging sink configuration (no code change needed to
set a retention policy).

**Rationale**:

- The constitution requires no silent failures and demands observability; a structured log entry
  satisfies both without introducing a new database table, migration, or repository.
- Serilog with structured properties is already the project's logging standard (Serilog is a
  declared backend dependency); `SpatialQueryAuditLogger` is a thin wrapper that calls
  `_logger.Information("SpatialQueryExecuted {@AuditEntry}", entry)`.
- The log entry is immutable by definition (append-only log sink); end users cannot delete log
  entries, satisfying the compliance retention requirement (clarification Q5).
- `ReturnedFeatureIds` is serialized as a JSON array in the structured property; for large result
  sets this may be large but it is a compliance requirement and is not shown in UI.

**Alternatives considered**:

- *New `SpatialQueryAuditLog` database table* — rejected: requires a migration and a repository;
  adds infrastructure dependency without adding capability the structured log cannot provide. If
  a future compliance requirement mandates queryable audit logs, a migration can be added then.
- *Error-only logging* — rejected: clarification Q5 explicitly selected full audit trail.

---

## D6 — Dataset Catalog as an in-memory, DI-registered service

**Decision**: `IDatasetCatalog` is an Application interface backed by `DatasetCatalog`, an
in-memory dictionary of `DatasetEntry` records registered as a singleton in DI. The initial
catalog contains four entries: `land-parcels`, `buildings`, `roads`, `development-areas`.
Adding a new dataset requires registering a `DatasetEntry` and an `ISpatialQueryProvider`
implementation — no other code changes.

**Rationale**:

- The spec requires (SC-008) that a new dataset is queryable by catalog registration alone;
  an in-memory singleton satisfies this without a database-backed catalog that would require
  a migration and an admin UI for v1.
- `DatasetEntry` describes: `Id`, user-facing `DisplayName`, `SupportedAttributeFields`
  (name, type, unit), `SupportedSpatialRelationships`, and `ProviderId` (which
  `ISpatialQueryProvider` handles it). The specialist resolves the entry from the catalog and
  picks the matching provider.

**Alternatives considered**:

- *Database-backed catalog* — deferred: needed if admin-managed datasets become a requirement;
  out of scope for v1 per spec Assumptions.
- *Hard-coded switch statement in the specialist* — rejected: violates OCP and requires code
  change per new dataset, explicitly prohibited by SC-008.
