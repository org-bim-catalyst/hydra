# Phase 0 Research: Buildings-Only Map Style

## Decision 1: Apply buildings-only as a style array on top of ROADMAP, not a new `MapTypeId`

**Decision**: "Buildings only" is implemented as `map.setOptions({ mapTypeId:
google.maps.MapTypeId.ROADMAP, styles: BUILDINGS_ONLY_STYLE })`, where
`BUILDINGS_ONLY_STYLE` is a fixed `google.maps.MapTypeStyle[]` hiding the `road`, `poi`,
`transit`, `administrative`, and `landscape.natural` feature types (`stylers: [{
visibility: 'off' }]`), mirroring the categories in the user-supplied reference snippet.

**Rationale**: `google.maps.MapTypeStyle` (the `styles` option) only composes with the
raster `ROADMAP`/`SATELLITE`(labels only)/`HYBRID`/`TERRAIN` map types — it is not a
`MapTypeId` value itself. There is no `MapTypeId.BUILDINGS_ONLY`. Layering it on
`ROADMAP` gives the clearest "buildings only" read (satellite imagery is too visually
busy to selectively hide categories from; hybrid duplicates that problem).

**Alternatives considered**:
- *A `TERRAIN` base instead of `ROADMAP`*: rejected — `TERRAIN` foregrounds elevation
  shading, competing with the goal of building shapes reading as the dominant content.
- *Applying the style on top of the user's currently-selected base type* (e.g.
  buildings-only-satellite): rejected as unnecessary scope — the spec calls for one new
  named option, not a modifier compatible with every existing style; ROADMAP is the only
  sensible carrier.

## Decision 2: Gate visibility of the option on the existing `VITE_GOOGLE_MAPS_MAP_ID` build-time env var, not a runtime Maps API capability check

**Decision**: Whether "Buildings only" is offered is decided statically, at the same
point `MapRenderTarget.tsx` today decides whether to pass `mapId` into
`createGoogleMapsGisLayer`: if `VITE_GOOGLE_MAPS_MAP_ID` is unset (raster rendering),
the option is offered; if it is set (vector rendering, per the existing doc comment on
`GoogleMapsGisLayer.mapId`), it is not.

**Rationale**: The Google Maps JS API does not expose a documented, stable runtime
signal for "is this specific map instance rendering vector or raster" that is cheaper or
more reliable than the fact this codebase already has: whether a Map ID was configured.
`GoogleMapsGisLayer`'s existing `colorScheme` doc comment already establishes that
`mapId` presence is this codebase's working proxy for vector vs. raster, so reusing it
avoids inventing a second, inconsistent way to ask the same question.

**Alternatives considered**:
- *Always show the option; let it silently no-op on vector maps*: rejected — directly
  violates FR-006 and the constitution's transparency principle (no silently broken
  feature).
- *Detect vector rendering by inspecting the live `google.maps.Map` instance at
  runtime*: rejected — no documented public API for this exists as of the Maps JS API
  version this codebase loads (`version: 'weekly'` in `GoogleMapsGisLayer.getLoader`);
  would mean depending on undocumented internals for a check the build-time env var
  already answers correctly today.

## Decision 3: Extend `MapStyleId` with a fourth literal value, `'buildings-only'`

**Decision**: `export type MapStyleId = 'roadmap' | 'satellite' | 'hybrid' |
'buildings-only'`, threaded through the same store/engine/layer path the existing three
values already use — no parallel type or separate command.

**Rationale**: Matches Constitution §II (OCP: extend the existing closed set via a new
variant, not a new mechanism) and keeps `viewerEngineStore.mapStyle`,
`ViewerEngine.setMapStyle`, and the `mapStyleChanged` event exactly as they are today —
only the union's member count changes, not any function signature.

**Alternatives considered**:
- *A separate boolean flag (`buildingsOnly: boolean`) orthogonal to `mapStyle`*:
  rejected — the spec's acceptance scenarios treat it as one of four mutually exclusive
  choices in the same menu (selecting roadmap/satellite/hybrid fully replaces it), so a
  fourth enum member models the actual behavior more directly than a cross-cutting flag
  would, and avoids the invalid combined states a boolean-plus-enum design would allow
  (e.g. `satellite` + `buildingsOnly: true`, which is out of scope per Decision 1).

## Decision 4 (post-implementation follow-up): support "Buildings only" on vector rendering too, via a second cloud-styled Map ID

**Decision**: Rather than permanently omitting "Buildings only" on any vector-rendering
deployment (Decision 2's original scope), a second Map ID
(`VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID`) can be configured with a cloud-based style
(`docs/google-maps-styles/buildings-only-{light,dark}.cloud.json`, using Google's
current cloud-based-maps-styling JSON schema) that hides everything except building
footprints. When configured, selecting `'buildings-only'` recreates the map with this
Map ID instead of the base one (`MapRenderTarget.tsx`); `isBuildingsOnlyStyleSupported()`
now returns `true` for this case too. `'buildings-only'` is omitted from the menu only
when a base Map ID is configured (vector rendering) but no buildings-only Map ID exists
— the one remaining case where it would silently do nothing.

**Rationale**: Google fully retired import/save support for the legacy array-of-rules
JSON format in Cloud Console's Map Style editor on 2025-03-25 — the client-side `styles`
array (Decision 1) genuinely cannot be replicated as a cloud style using that old
schema. The current cloud-based-styling schema (`{ variant, backgroundColor, styles:
[{ id, geometry, label }] }`, keyed by feature ids like `infrastructure.building`,
`political`, `natural.water`, `pointOfInterest`) is a different, still-actively-
supported mechanism that achieves the same visual result on a vector map. A single Map
ID's Light/Dark style variants cover both themes — no second Map ID per theme is
needed, mirroring how `colorScheme` already selects a variant on the base Map ID.

**Alternatives considered**:
- *Leave vector deployments permanently unsupported (original Decision 2 scope)*:
  superseded once a working cloud-styling path was confirmed to exist — no longer the
  best available option now that one does.
- *A live `map.setOptions({ mapId })` call instead of recreating the layer*: rejected —
  no such live setter exists; Map ID, like `colorScheme`, can only be set when a map is
  initialized (per `@types/google.maps`), so `MapRenderTarget` must recreate the whole
  layer, exactly as it already does for a light/dark theme toggle.

## Open questions

None remaining.
