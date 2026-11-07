# Quickstart: Spatial Query Analysis (SPEC-095)

**Branch**: `095-spatial-query-analysis` | **Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

This guide documents the runnable validation scenarios that prove the feature works end-to-end.
Refer to [data-model.md](data-model.md) and [contracts/](contracts/) for type and event details.

---

## Prerequisites

- Spec 094 (Spatial Analysis Orchestrator) implemented and passing its own tests.
- `SpatialOperationKinds.Supported` extended with the `spatial.query` entry.
- `SpatialQuerySpecialist` registered in `DependencyInjection.cs`.
- At least one `ISpatialQueryProvider` registered with `SupportedDatasetIds` including `buildings`.
- `DatasetCatalog` populated with the initial four entries (land-parcels, buildings, roads, development-areas).
- Backend running (`dotnet run` from `src/AskLucy.Web`).
- Authenticated user session active in the browser.

---

## Scenario 1 — Attribute Filter: Buildings taller than 30 m

**What this proves**: FR-001, FR-002, FR-007, FR-008, FR-010, SC-001, SC-005.

### Steps

1. Open the chat and type:
   > Show me all buildings taller than 30 metres in Al Khuwair

2. Observe the coordinator's interpretation in the chat response — it should identify:
   - Dataset: **Buildings**
   - Attribute condition: `height > 30 m`
   - Study area: **Al Khuwair** (resolved via location service)

3. Within 3 seconds of sending, the first batch of matching building footprints must appear as
   highlighted polygons on the map in a layer named something like "Buildings > 30 m in Al Khuwair".

4. The legend must display a running feature count and a streaming progress indicator while batches
   are still arriving. Once streaming completes, the indicator is replaced by the final count.

5. Click one of the highlighted buildings. The **Feature Attribute Panel** must open within 2 seconds
   showing at minimum: Height (m), Floors, Year Built, Use.

### Expected Outcome

- Layer named after the query visible on the 2D map.
- Only buildings satisfying `height > 30 m` are highlighted; buildings ≤ 30 m are not highlighted.
- Feature count in legend matches the number of visible building polygons.
- Attribute panel shows correct attribute values for the clicked building.

---

## Scenario 2 — Spatial Relationship Query: Roads intersecting a zone

**What this proves**: FR-003, FR-007, FR-008, FR-009, SC-002.

### Steps

1. Ensure a development zone boundary is active on the map (or name one explicitly).
2. Type:
   > Which roads intersect the Al Khuwair Development Zone?

3. Confirm Lucy identifies:
   - Dataset: **Roads**
   - Spatial predicate: `intersecting` the named feature "Al Khuwair Development Zone"

4. Result layer "Roads intersecting Al Khuwair Zone" appears on the map.

### Expected Outcome

- Only roads whose geometry intersects the zone boundary are highlighted.
- Roads entirely outside the zone are absent from the result layer.
- The layer is independently hideable and removable from the legend.

---

## Scenario 3 — Combined Query: Parcels by area AND spatial containment

**What this proves**: FR-004, SC-002, User Story 3.

### Steps

1. Type:
   > Show me land parcels larger than 5000 m² that fall within the designated development area

2. Confirm Lucy identifies:
   - Dataset: **Land Parcels**
   - Attribute predicate: `area > 5000 m²`
   - Spatial predicate: `within` the development area
   - Both predicates active simultaneously

3. Result layer appears with parcels satisfying **both** conditions.

### Expected Outcome

- Parcels satisfying only the area condition (but outside the zone) are absent.
- Parcels satisfying only the spatial condition (but ≤ 5000 m²) are absent.
- Only parcels satisfying both conditions are highlighted.

---

## Scenario 4 — Clarification: Incomplete query

**What this proves**: FR-005, SC-003, User Story 4.

### Steps

1. Type:
   > Find buildings built before 1980

   (No geographic scope specified; the map is not centred on a recognisable area.)

2. Observe Lucy's response — it must ask for the geographic area **before** executing the query.
   No result layer must appear on the map.

3. Reply with a city or area name.

4. After the clarification, the query runs and results appear.

### Expected Outcome

- No partial result layer appears before the user clarifies.
- Lucy's clarification question is focused (one question only).
- After clarifying, results appear without further prompting.

---

## Scenario 5 — Large result set: Streaming progress

**What this proves**: SC-001, clarification Q1 (paginated streaming), edge-case timeout rule.

### Steps

1. Type a broad query likely to return many features:
   > Show me all roads in Al Khuwair

2. Observe:
   - First features appear on the map within 3 seconds.
   - The legend shows a live feature count incrementing as batches arrive.
   - A streaming progress indicator is visible while `isFinal = false`.
   - When streaming completes, the progress indicator is replaced by the final count.

3. (Timeout simulation — optional): If a timeout can be forced during development by reducing the
   30-second limit in configuration, verify the user receives a notification and no partial layer
   is left in a broken state.

### Expected Outcome

- First batch visible within 3 seconds.
- Final legend count matches the total returned features.
- On timeout: user is notified; map shows a clean state (no broken partial layer).

---

## Scenario 6 — Layer management

**What this proves**: FR-009, FR-013, SC-004, SC-006, User Story 5.

### Steps

1. Run Scenario 1 (buildings query) to create Layer A.
2. Run Scenario 2 (roads query) to create Layer B.
3. In the legend, hide Layer A — only Layer A's polygons disappear; Layer B is unaffected.
4. Remove Layer B using the legend's remove button — Layer B disappears; Layer A (hidden) remains.
5. Unhide Layer A — it reappears.
6. Switch from 2D to 3D view — Layer A must still be visible and correctly positioned.
7. Click "Clear all" — Layer A is removed; the legend is empty.

### Expected Outcome

- Each layer is independently controllable.
- Switching between 2D and 3D preserves visibility state.
- "Clear all" removes all layers simultaneously without leaving orphaned entries.

---

## Scenario 7 — Error: Dataset unavailable

**What this proves**: FR-012, SC-007, edge case (unavailable dataset).

### Steps

1. Simulate a provider being unavailable (e.g., configure the provider to return an error or
   temporarily remove its registration).
2. Type a query targeting that dataset:
   > Show me land parcels larger than 5000 m²

### Expected Outcome

- No result layer is added to the map.
- Lucy provides a clear, user-readable explanation that the dataset is unavailable.
- No silent failure; no console-only error.

---

## Scenario 8 — Audit log verification

**What this proves**: FR-015, SC-009, clarification Q5.

### Steps

1. Run Scenario 3 (combined query) with a test user.
2. Access the structured log (stdout / log sink output during development).
3. Search for a log entry with `auditEventType = "SpatialQueryExecuted"`.

### Expected Audit Entry Fields

- `userId` matches the test user.
- `datasetId = "land-parcels"`.
- `queryDefinition` contains both the attribute predicate (`area > 5000 m²`) and the spatial predicate (`within development-area`).
- `outcome = "Success"`.
- `durationMs` is a positive integer.
- `resultCount` matches the number of features rendered on the map.
- `returnedFeatureIds` is a non-empty JSON array.
