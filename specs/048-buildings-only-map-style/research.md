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

## Open questions

None remaining — all three research items above resolve the plan's technical unknowns;
no `NEEDS CLARIFICATION` markers remain from the spec or the Technical Context section.
