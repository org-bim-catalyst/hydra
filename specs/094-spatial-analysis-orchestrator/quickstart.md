# Quickstart: Spatial Request Bundle & Analysis Orchestrator

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Runnable validation scenarios that prove the feature end-to-end. See
[data-model.md](data-model.md) for field rules and [contracts/](contracts/) for payload shapes.

## Prerequisites

- .NET 10 SDK and Node (ClientApp) as for the rest of the repository.
- The shared test database variables if running the full backend suite (see repository `docs/TESTING.md`;
  never print the connection strings).
- An active chat with a provider configured.

## Build and test

```powershell
# Backend
dotnet build "Ask Lucy.sln"
dotnet test  tests/AskLucy.Application.Tests
dotnet test  tests/AskLucy.Web.Tests

# Frontend (from src/AskLucy.Web/ClientApp)
npm test
npx tsc -b --noEmit   # the root tsconfig uses project references; bare --noEmit checks nothing
```

## Scenario 1 — One coordinated POI answer (US1)

1. Start the app and open a chat with the map visible.
2. Ask: **"Find hospitals in Muscat."**
3. Expect:
   - exactly one `__SPATIAL_LAYERS__` event with `status: "Complete"` and one `markers` layer;
   - markers rendered, legend showing "Hospitals";
   - no `__POI_LAYERS__` event anywhere in the stream (the old path is retired).

## Scenario 2 — Two independent operations (US2)

1. Ask: **"Find schools near hospitals and highlight areas more than 1 km from a clinic."**
2. Expect:
   - two specialist outcomes merged into one analysis (one markers layer, one buffer/area layer);
   - each layer named and independently controllable;
   - the two operations observed to start before either finishes (concurrency assertion in the unit test fakes).

If proximity is not yet registered (spec 093 not landed), expect the POI result plus a visible "not performed"
warning for the proximity operation — never a silent omission.

## Scenario 3 — Implicit location from the map (US3)

1. Pan/zoom to an area; do **not** name a place.
2. Ask: **"Show me hospitals here."**
3. Expect:
   - the study area taken from the current map view;
   - the response stating the resolved area;
   - the same result as naming that area explicitly.

Then ask **"Show me hospitals here"** with the map focused nowhere useful and no active location — expect a
focused "which area?" question, not a guessed location.

## Scenario 4 — Layer lifecycle (US4)

1. From Scenario 1, hide the layer in the legend → markers disappear; legend matches.
2. Show it again → markers return.
3. Ask for a different category with `layerMode` unspecified → expect the clear-or-keep question before the
   second search runs.
4. Answer "keep" → both layers present and distinguishable; "replace" → only the new one.
5. Remove one layer, then clear all → map empty, conversation history intact.
6. Switch 2D ↔ 3D → layers remain available and controllable.

## Scenario 5 — Failure isolation (US5)

1. Run a two-operation request with one specialist forced to fail (fake/test configuration).
2. Expect:
   - the successful layer delivered;
   - a visible statement naming the failed operation;
   - `status: "Partial"` with a warning and coverage entry.
3. Force **all** specialists to fail → expect an explicit failure narration, no layer event, and a structured log
   entry naming the bundle, operation and specialist.

## Scenario 6 — Empty is not failure (US5)

1. Search a category in an area known to have none.
2. Expect `status: "Empty"`, a plain "none found" narration, and no error.

## Scenario 7 — Add a specialist (US6 / SC-009)

1. Add a test specialist implementing `ISpatialSpecialist` for a new `operationKind`, register it in DI, and add
   its interpretation wording to the coordinator's supported-kind declaration.
2. Request that operation.
3. Expect it to be interpreted, dispatched, normalized and rendered without edits to
   `SpatialAnalysisCoordinator`, `SpatialResultNormalizer`, `SpatialAnalysisCapability`, the SSE writer or the
   client store.

## Scenario 8 — Map context is untrusted (SC-009 / FR-017)

1. Send a chat request with a malformed `mapContext` (NaN coordinate, inverted bounds, zoom out of range).
2. Expect a user-visible validation message and no provider call.

## Expected automated coverage

- Application unit tests: bundle version rejection, validation, registry lookup, concurrency, dependency order,
  merge/coverage, failure isolation, provider failure ≠ empty, context resolution.
- Web contract tests: capability result → `ChatStreamChunk.SpatialLayers`; controller writes and flushes
  `__SPATIAL_LAYERS__`.
- Frontend: `spatialLayersStore` apply/keep/replace/remove/clear and cross-chat isolation; SSE parsing in
  `aiApi.ts` and both `useChatStream` branches; overlay/legend rendering and the parse-failure fallback.
