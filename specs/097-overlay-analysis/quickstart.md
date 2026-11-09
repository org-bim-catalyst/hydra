# Overlay Analysis Quickstart

## Prerequisites

- Use the repository's .NET 10 SDK and Node 24 toolchain.
- Use deterministic parcel, development-area, road, and facility fixtures exposed through registered `ISpatialQueryProvider` implementations. The initial catalog does not register a population-density dataset; population criteria require a separately registered source.
- Geographic features and study areas use WGS84 `GeoPoint` values. The current provider contract carries no per-source CRS metadata, so mismatched CRS inputs cannot be detected or transformed and must not be mixed.
- Review [the overlay contract](contracts/overlay-analysis.contract.md) and the existing spatial bundle/result event contracts before changing serialized payloads.

## Validation scenarios

1. **Eligibility**: request available parcels with a declared numeric attribute criterion and a road-distance limit in a bounded fixture. Confirm every result satisfies all hard criteria. Population-density scenarios need an added catalog/provider fixture.
2. **Geometry operators**: verify intersection, union, directional difference, and spatial intersection selection against known point, line, and polygon geometries; ensure difference direction changes output as expected.
3. **Clarification**: omit the study area. Confirm Lucy asks and retains the typed overlay request; after an area answer it runs without asking the model to reconstruct the datasets and steps. Difference direction is explicit in the ordered input list: input 1 minus input 2.
4. **Ranking**: provide declared weights and fixed criterion values. Recompute normalized scores independently and confirm exact score ordering and disclosed effective weights. Confirm an ineligible candidate never ranks.
5. **Coverage/errors**: simulate unavailable source, invalid geometry, incomplete query stream, provider failure, empty results, cancellation, and a single specialist failure in a multi-operation request. Confirm invalid features make source coverage partial, an empty eligible set remains empty even when source layers render, and failures are visible. CRS mismatch tests are unavailable until the query contract carries CRS metadata.
6. **Viewer lifecycle**: exercise point, line and polygon feature payload validation, selection event dispatch, per-layer toggle/removal, score/provenance inspection, legend coverage, and shared layer-store state across viewer modes. Google Maps rendering itself needs a browser/API-key integration environment; the unit suite validates payloads and overlay lifecycle contracts.

## Commands

```powershell
dotnet test tests/AskLucy.Application.Tests/AskLucy.Application.Tests.csproj --filter "FullyQualifiedName~Spatial.Overlay"
dotnet test tests/AskLucy.Infrastructure.Tests/AskLucy.Infrastructure.Tests.csproj --filter "FullyQualifiedName~Spatial.Overlay"
Set-Location src/AskLucy.Web/ClientApp
npm run test -- --run src/store/spatialLayersStore.test.ts src/features/chat/api/aiApi.test.ts src/features/viewer/components/FeatureAttributePanel.test.tsx src/features/viewer/components/SpatialLayerLegend.test.tsx src/features/viewer/components/SpatialLayersOverlay.test.tsx src/features/viewer/components/ViewerSurface.test.tsx
npm run lint
npm run build
```

For full CI validation, follow `.github/workflows/ci.yml`; web and persistence integration tests require their configured SQL Server test connections.

## Execution notes (2026-11-08)

- Focused backend overlay tests pass for application and infrastructure projects.
- Focused frontend contract/viewer tests, lint, and production build pass. Lint reports the existing fast-refresh warning for `styleOf` exported beside `SpatialLayersOverlay`.
- The full frontend test command was started but stopped after several minutes because the repository-wide accessibility suite was still processing; no failing assertion had appeared. No live browser/API-key or SQL Server scenario was run.
- The local catalog has no population-density dataset, the provider contract has no CRS field, and no live infrastructure-backed dataset environment was configured; those quickstart scenarios need those prerequisites.
