# Data Model: Immersive Viewer Platform for AI-Assisted Urban Design

Phase 1 output for [plan.md](./plan.md). Per spec FR-012b, this feature introduces **no new database
entities or persistence** — everything below is client-session state (Zustand) or a transient
request/response DTO shape (backend). Where an entity below corresponds to a spec Key Entity, the spec
name is noted.

## Client-side state (`viewer/store/viewerEngineStore.ts`)

Session-scoped only (no `persist` middleware), following the `workspaceOverlayStore` convention —
every visit to the workspace starts on the placeholder.

### ViewerSession (spec: *Viewer Session*)

| Field | Type | Notes |
|---|---|---|
| `contentMode` | `'placeholder' \| 'map'` | Which render target (research.md Decision 3) is mounted. Starts `'placeholder'`. |
| `camera` | `CameraViewState` | See below. |
| `selection` | `SelectionState` | See below. |
| `layers` | `RenderLayer[]` | Currently-registered layers (FR-002/FR-021 `addLayer`/`removeLayer`). |

### CameraViewState (spec: *Camera/View State*)

| Field | Type | Notes |
|---|---|---|
| `mode` | `'isometric' \| 'plan'` | Renamed from the existing `viewMode: '2D'\|'3D'` field (research.md Decision 4). Default `'isometric'`. |
| `rotationEnabled` | `boolean` | Default `true`, unless `prefersReducedMotion` is set at mount (FR-016), in which case default `false`. |

### SelectionState (spec: *Selection State*)

| Field | Type | Notes |
|---|---|---|
| `selectedLayerId` | `string \| null` | The currently highlighted element's owning layer id, if any. |
| `selectedElementId` | `string \| null` | Null when nothing is selected (FR-019 / US5-AC3). Single-selection for this feature; the shape leaves room for multi-select later without a breaking change (an array wrapper), not built now (YAGNI). |

### RenderLayer (spec: *Render Layer*, base for *GIS/Map Layer*, *Model/Drawing Layer*, *Overlay*)

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | Caller-supplied or engine-generated unique id. |
| `kind` | `'gis' \| 'model' \| 'overlay'` | Discriminates the three layer families from spec FR-003/FR-020. |
| `visible` | `boolean` | Show/hide without removing (FR-002 "added, removed, shown, or hidden"). |
| `zIndex` | `number` | Composition order (e.g., overlays above map/model layers). |
| `metadata` | `Record<string, unknown>` | Layer-kind-specific data (e.g., a GIS layer's `center`/`zoom`; opaque to the engine core). |

- **GisMapLayer** (`kind: 'gis'`): `metadata` holds `{ provider: 'google-maps', center: { latitude, longitude }, zoom }`. Exactly one is expected active in this feature (the current-location layer); the shape supports more later (ESRI/OSM per spec Assumptions) without an engine change.
- **ModelLayer** (`kind: 'model'`): contract-only in this feature — `metadata` shape intentionally unspecified beyond "not populated with real content" (spec Key Entities); exists so `addLayer({ kind: 'model', ... })` is a real, testable no-op-but-valid command today (US6/FR-021).
- **Overlay** (`kind: 'overlay'`): `metadata` holds arbitrary visualization data; rendered above whatever base layer(s) are active without mutating them (FR-020).

## Backend DTOs (`AskLucy.Application/Weather`)

No new EF Core entities — `AskLucy.Domain`/`AskLucy.Persistence` are unaffected.

### WeatherSnapshotDto (spec: *Weather Snapshot*)

| Field | Type | Notes |
|---|---|---|
| `LocationName` | `string` | Reverse-geocoded/provider-supplied place name for the queried coordinates. |
| `TemperatureCelsius` | `double` | |
| `Condition` | `WeatherCondition` (enum) | `Clear \| PartlyCloudy \| Cloudy \| Fog \| Rain \| Snow \| Thunderstorm \| Windy` (research.md Decision 7). |
| `IsDaytime` | `bool` | Drives day/night icon variant. |
| `ObservedAtUtc` | `DateTimeOffset` | Upstream provider's observation time — the frontend uses this to compute/display staleness (FR-011), not a server-persisted value. |

### GetCurrentWeatherQuery (MediatR, `Application/Weather/Queries/GetCurrentWeather`)

| Field | Type | Notes |
|---|---|---|
| `Latitude` | `double` | Validated range [-90, 90] (`GetCurrentWeatherQueryValidator`, FluentValidation — matches existing handler-validation convention). |
| `Longitude` | `double` | Validated range [-180, 180]. |

Returns `WeatherSnapshotDto`. Failure modes surface as typed exceptions
(`WeatherProviderUnavailableException` → 502; a validation failure → 400 via the existing
`ValidationException` → Problem Details mapping) — no new generic error shapes, reusing
`ProblemDetailsMiddleware`'s existing `Map()` switch (one new arm).

## Client-side session state (not persisted, spec: *User Location*, *Weather Snapshot*)

| Field | Type | Notes |
|---|---|---|
| `UserLocation.latitude` / `.longitude` | `number` | From `useGeolocation()`, held in a TanStack Query cache entry or local component state — never written to `localStorage`/backend (FR-012b). |
| `UserLocation.resolvedAt` | `number` (epoch ms) | Used only to decide when to re-resolve, not displayed. |
| `WeatherSnapshot` (frontend) | mirrors `WeatherSnapshotDto` + a derived `isStale: boolean` | Computed client-side from `ObservedAtUtc` vs. now against a threshold (implementation detail, e.g. >30 min old). |

## Viewer Command / Viewer Event

Full typed contract in [contracts/viewer-engine-api.md](./contracts/viewer-engine-api.md); summarized
here as the two entities from spec Key Entities:

- **ViewerCommand**: a discriminated union of instructions (`addLayer`, `removeLayer`, `zoomToLocation`,
  `select`, `clearSelection`, `displayContent`, `createOverlay`, `setViewMode`, `setRotationEnabled`),
  each returning a `{ ok: true, ... } | { ok: false, error: string }` outcome synchronously or via a
  resolved Promise — never throwing for expected failure cases (FR-022), matching this codebase's
  "no silent failures" discipline at the API boundary.
- **ViewerEvent**: a discriminated union of notifications (`layerAdded`, `layerRemoved`,
  `contentLoaded`, `selectionChanged`, `viewModeChanged`, `rotationChanged`) emitted on a simple
  pub/sub (`on`/`off`) so any subscriber (this feature's own UI, a future AI agent, analytics) can
  observe state without polling (FR-023).
