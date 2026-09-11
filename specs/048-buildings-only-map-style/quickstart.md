# Quickstart: Validating Buildings-Only Map Style

## Prerequisites

- `src/AskLucy.Web/ClientApp` dependencies installed (`npm install`).
- A valid Google Maps Platform API key configured for the dev client (existing
  requirement for the viewer's map/GIS content mode — not new to this feature).
- To exercise the "option not offered" path (User Story 3 / FR-006), a build with
  `VITE_GOOGLE_MAPS_MAP_ID` unset vs. set are both needed.

## Automated validation

```bash
cd src/AskLucy.Web/ClientApp
npm test -- viewer/api viewer/store viewer/engine viewer/layers/gis features/chat/workspaceControls
```

Expected: all existing map-style tests continue to pass, plus new assertions covering:
- `MapStyleId` accepts `'buildings-only'` (type-level, exercised via
  `ViewerEngine.test.ts` / `ViewerEngine.contract.test.ts`).
- `GoogleMapsGisLayer.setMapTypeId('buildings-only')` calls `map.setOptions` with
  `mapTypeId: ROADMAP` and the buildings-only `styles` array (`GoogleMapsGisLayer.test.ts`).
- Switching from `'buildings-only'` to any other style clears `styles` back to `[]`
  (`GoogleMapsGisLayer.test.ts`).
- `useMapStyleControl` includes a `'buildings-only'` action, highlighted only when
  active, when raster rendering is active; omits it entirely when vector rendering is
  active (`workspaceControls.test.tsx`, if/when such a test file is added alongside this
  feature — see tasks.md).

## Manual / browser validation (raster deployment — `VITE_GOOGLE_MAPS_MAP_ID` unset)

1. Run the dev client (`npm run dev`) with no `VITE_GOOGLE_MAPS_MAP_ID` set.
2. Open the viewer, switch to map/GIS content mode, open the map style menu.
3. Confirm four options are present: Road map, Satellite, Hybrid, Buildings only.
4. Select "Buildings only" — confirm roads, POI icons, transit lines, administrative
   borders/labels, and natural landscape disappear; building footprints remain visible;
   the menu highlights "Buildings only" as active.
5. Pan/zoom/rotate the map — confirm the buildings-only look persists.
6. Search a different location — confirm the buildings-only look persists after
   navigating.
7. Select "Road map" — confirm the map returns to the normal road map look with no
   leftover hidden categories.

## Manual / browser validation (vector deployment — `VITE_GOOGLE_MAPS_MAP_ID` set)

1. Run the dev client with a valid vector-enabled `VITE_GOOGLE_MAPS_MAP_ID` set.
2. Open the map style menu — confirm only Road map, Satellite, and Hybrid are present;
   "Buildings only" is not offered (FR-006 / SC-003).
