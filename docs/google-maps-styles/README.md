# Google Maps cloud styles — Buildings Only (vector rendering)

Companion artifacts to specs/048-buildings-only-map-style. Google's client-side JSON
`styles` option (`GoogleMapsGisLayer.BUILDINGS_ONLY_STYLE`) has no effect on a vector
map — one with a `mapId`, which is what `VITE_GOOGLE_MAPS_MAP_ID` configures for this
deployment. The equivalent look on a vector map has to come from a style attached to a
Map ID in Google Cloud Console instead.

## Paste these into Cloud Console: the `.cloud.json` files

- `buildings-only-light.cloud.json` — paste into the style editor for a **light**-theme
  Map ID (Google Cloud Console → Google Maps Platform → Map Management → your Map ID →
  Map Style → Edit → Import/paste JSON).
- `buildings-only-dark.cloud.json` — same, for a **dark**-theme Map ID.

These use Google's current **cloud-based maps styling JSON schema** — a `{ variant,
backgroundColor, styles: [{ id, geometry, label }] }` shape keyed by feature ids like
`infrastructure.building`, `political`, `natural.water`, `pointOfInterest` — which is
the *only* format Cloud Console's Map Style editor accepts today. Google retired
import/save support for the older array-of-rules ("legacy") JSON format on March 25,
2025; pasting that older format now produces a "You are using the Google Maps Legacy
JSON format" warning and the style cannot be saved.

Each `.cloud.json` file:

- Hides `pointOfInterest`, `political` (administrative borders/labels), and `natural`
  (land/landcover) entirely — geometry and labels both off.
- Re-enables `natural.water` with a theme-appropriate fill color, labels off.
- Re-enables `natural.land.landCover` with a flat fill matching the background, labels
  off (a bare canvas, not a hole in the map).
- Hides `infrastructure` (which also covers every road/rail/transit sub-id) entirely,
  then re-enables only `infrastructure.building` (+ `.commercial`) — the feature id
  for building footprints — with `BUILDING_FOOTPRINT_FILL_COLOR`/`_STROKE_COLOR`,
  labels off.

## `buildings-only-light.json` / `-dark.json` — legacy format, reference only

These two files use the **older**, array-of-rules `MapTypeStyle[]` JSON shape (a list
of `{ featureType, elementType, stylers }` objects). This is a *different, still-valid*
API: the client-side `google.maps.Map` `styles` option (`map.setOptions({ styles })`),
used by `GoogleMapsGisLayer.BUILDINGS_ONLY_STYLE` for the **raster** rendering path.
They document/mirror that raster style in one place; they are not accepted by Cloud
Console's Map Style editor for a vector Map ID and should not be pasted there.

## Building color is fixed, not themed

All four files paint building footprints (`landscape.man_made` in the legacy schema,
`infrastructure.building`/`infrastructure.building.commercial` in the cloud schema)
with the exact same two colors — `#FF00FF` fill / `#B300B3` stroke — regardless of
light or dark theme or rendering path. These match
`viewer/layers/gis/buildingFootprintColors.ts`'s
`BUILDING_FOOTPRINT_FILL_COLOR`/`BUILDING_FOOTPRINT_STROKE_COLOR` constants, also used
by the raster path's `GoogleMapsGisLayer.BUILDINGS_ONLY_STYLE`.

This is deliberate: a planned footprint-extraction algorithm will color-key rendered
map tiles for this exact fill color to find building 2D shapes. Keeping it identical
everywhere means that algorithm never needs to know which theme or rendering path
produced the tile it's reading — one constant, one color.

## Wiring this up in the client — done (research.md Decision 4)

One Map ID carries **both** the light and dark `.cloud.json` styles as its Light/Dark
style variants (Cloud Console → Map Management → the Map ID → Map styles → Light
mode / Dark mode) — no separate Map ID per theme is needed; `colorScheme` picks the
variant the same way it already does for the base Map ID.

- `VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID` holds that one Map ID.
- `MapRenderTarget.tsx` recreates the whole map with this Map ID (instead of
  `VITE_GOOGLE_MAPS_MAP_ID`) whenever `mapStyle` is `'buildings-only'`, and recreates
  back to the base Map ID when it isn't — Map ID (like `colorScheme`) can only be set
  when a map is initialized, so this follows the same recreate-the-layer pattern
  already used for a light/dark theme toggle, not a live `map.setOptions({ mapId })`
  call (no such live setter exists). Switching among roadmap/satellite/hybrid never
  triggers this recreation — only crossing the buildings-only boundary does.
- `isBuildingsOnlyStyleSupported()` (`viewer/api/commands.ts`) now offers "Buildings
  only" whenever either rendering path can actually show it: no Map ID at all
  (raster, client-side style), or a Map ID **and** this buildings-only Map ID both
  configured (vector, cloud style swap). It's omitted only when a Map ID is
  configured but no buildings-only Map ID exists — the one case where selecting it
  would silently do nothing.
