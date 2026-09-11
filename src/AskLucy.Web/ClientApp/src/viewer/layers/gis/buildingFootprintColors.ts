/** specs/048-buildings-only-map-style: the fixed, deterministic fill/stroke colors a future
 * building-footprint-extraction algorithm will color-key against in a rendered map tile.
 *
 * These values are NOT applied from client code — Google Maps' JSON `styles` option has no
 * effect on a vector map (one with a `mapId`), which is why `GoogleMapsGisLayer` only applies
 * `BUILDINGS_ONLY_STYLE` on the raster path (see `isBuildingsOnlyStyleSupported`). On a vector
 * deployment, the equivalent look instead comes from a cloud-configured style attached to a
 * dedicated Map ID in Google Cloud Console — see `docs/google-maps-styles/buildings-only-light.json`
 * and `buildings-only-dark.json`, which style `landscape.man_made` (Google's vector building-
 * footprint feature type) using exactly these two hex values.
 *
 * Deliberately identical across light and dark: a color-keying algorithm should not need to know
 * which theme's Map ID rendered the tile it's reading — only the base-map/water colors differ
 * between the two style JSON files, never the building colors. Chosen as fully-saturated,
 * non-cartographic hues (no natural or default Google Maps feature renders in magenta) so they
 * cannot be confused with genuine map content during color-based detection. */
export const BUILDING_FOOTPRINT_FILL_COLOR = '#FF00FF'
export const BUILDING_FOOTPRINT_STROKE_COLOR = '#B300B3'
