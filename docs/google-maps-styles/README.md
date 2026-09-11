# Google Maps cloud styles — Buildings Only (vector rendering)

Companion artifacts to specs/048-buildings-only-map-style. Google's client-side JSON
`styles` option (`GoogleMapsGisLayer.BUILDINGS_ONLY_STYLE`) has no effect on a vector
map — one with a `mapId`, which is what `VITE_GOOGLE_MAPS_MAP_ID` configures for this
deployment. The equivalent look on a vector map has to come from a style attached to a
Map ID in Google Cloud Console instead.

## Files

- `buildings-only-light.json` — paste into the JSON style editor for a **light**-theme
  Map ID (Maps Platform → Map Management → your Map ID → Map Style → Edit → paste JSON).
- `buildings-only-dark.json` — same, for a **dark**-theme Map ID.

Both hide the same categories as the raster path (road, POI, transit, administrative,
natural landscape) and differ only in their base ground/water colors, matching the
app's existing light/dark theming.

## Label categories are all off

Both files explicitly turn off labels for every category Cloud Console's categorized
style editor groups labels into — Political, Natural feature, Point of interest, and
Infrastructure — mapped onto this JSON schema's actual `featureType` values (Cloud
Console's four category names aren't literal `featureType`s in the raw JSON schema):

| Cloud Console category | `featureType`(s) used here |
|---|---|
| Political | `administrative`, `administrative.country/province/locality/neighborhood/land_parcel` |
| Natural feature | `landscape.natural`, `landscape.natural.landcover` |
| Point of interest | `poi` |
| Infrastructure | `road`, `transit` |

Most of these were already implied by the broader `visibility: "off"` rules on those
same feature types (an `elementType: "all"` rule already suppresses labels); the
explicit `elementType: "labels"` rules make that intentional rather than incidental,
and close one real gap — `landscape.natural.landcover`'s `elementType: "all"` rule
turns its geometry back **on**, which without a following labels-specific rule would
also have turned its labels back on. Any leftover label text would otherwise show up
as extra, non-magenta pixels inside or beside a building's color-keyed footprint.

## Building color is fixed, not themed

Both files paint `landscape.man_made` (Google's vector feature type for building
footprints) with the exact same two colors — `#FF00FF` fill / `#B300B3` stroke —
regardless of light or dark theme. These match the client-side raster style's colors
(`viewer/layers/gis/buildingFootprintColors.ts`, applied in
`GoogleMapsGisLayer.BUILDINGS_ONLY_STYLE`).

This is deliberate: a planned footprint-extraction algorithm will color-key rendered
map tiles for this exact fill color to find building 2D shapes. Keeping it identical
across both cloud styles and the raster style means that algorithm never needs to know
which theme or rendering path produced the tile it's reading — one constant, one
color, everywhere buildings are isolated this way.

## Wiring this up in the client (not yet done)

Once light/dark Map IDs exist with these styles attached, using them from
"Buildings only" on a vector deployment requires:

1. Two new env vars, e.g. `VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID_LIGHT` /
   `_DARK`, holding the two Map IDs above.
2. `GoogleMapsGisLayer`/`MapRenderTarget` recreating the map with the matching Map ID
   when `'buildings-only'` is selected — Map ID (like `colorScheme`) can only be set
   when a map is initialized, so this follows the same recreate-the-layer pattern
   `MapRenderTarget` already uses for a light/dark theme toggle, not a live
   `map.setOptions({ mapId })` call (no such live setter exists).

This is out of scope for the current change — tracked here so the next step is
concrete once the Map IDs exist.
