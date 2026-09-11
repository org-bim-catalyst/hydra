# Contract: Viewer Engine map-style surface (extended)

This feature extends the existing internal contract documented for the map/GIS content
mode's style control (`viewer/api/commands.ts`, `viewer/api/engine.ts`,
`viewer/api/events.ts`), exercised today by `ViewerEngine.contract.test.ts`. No HTTP/API
endpoints are involved — this is a frontend module contract.

## `MapStyleId` (command payload / event payload type)

```ts
export type MapStyleId = 'roadmap' | 'satellite' | 'hybrid' | 'buildings-only'
```

Backward compatible: existing values unchanged, one literal added.

## `ViewerEngine.setMapStyle(mapStyle: MapStyleId): ViewerCommandResult`

- Unchanged signature.
- Calling with `'buildings-only'`:
  - Updates `viewerEngineStore.mapStyle` to `'buildings-only'`.
  - Calls `activeTarget.applyMapStyle('buildings-only')` if a render target is mounted.
  - Emits `{ type: 'mapStyleChanged', mapStyle: 'buildings-only' }`.
  - Returns `{ ok: true, data: undefined }`, matching the existing three values' return
    shape — no new error path is introduced by this value (gating happens earlier, at
    the menu level, per FR-006 — the engine itself does not need to know about or reject
    the vector-rendering case, since the UI never offers the action there).

## `GoogleMapsGisLayerHandle.setMapTypeId(mapStyle: MapStyleId): void`

- Unchanged signature.
- New behavior for `'buildings-only'`:
  ```ts
  map.setOptions({
    mapTypeId: google.maps.MapTypeId.ROADMAP,
    styles: BUILDINGS_ONLY_STYLE,
  })
  ```
- New behavior for the other three values when switching *away from*
  `'buildings-only'`: must clear the style array (`styles: []`) alongside setting the
  target `MapTypeId`, so the buildings-only hiding does not linger on top of
  satellite/hybrid/roadmap (FR-004). Concretely, every branch of `setMapTypeId` uses
  `map.setOptions({ mapTypeId, styles })` uniformly (`styles` empty except for the
  `'buildings-only'` branch), rather than only the buildings-only branch calling
  `setOptions` and the others calling the narrower `map.setMapTypeId(...)`.

## Map style menu action list (`useMapStyleControl`)

- New action, `id: 'buildings-only'`, appended after `hybrid`, present in the returned
  `ExpandableActionGroupAction[]` **only when** raster rendering is active (no Map ID
  configured) — otherwise the array has exactly the existing three entries, unchanged.
- `highlighted: mapStyle === 'buildings-only'`, following the existing pattern.
