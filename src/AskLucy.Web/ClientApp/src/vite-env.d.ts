/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** Domain-restricted Google Maps Platform JS API key (specs/027-immersive-viewer-platform). */
  readonly VITE_GOOGLE_MAPS_API_KEY?: string
  /** A vector-rendering-enabled Map ID (Google Cloud Console → Maps Platform → Map Management),
   * with "Tilt" and "Rotation" enabled for that Map ID. Required for the map/GIS content mode's
   * 3D `WebGLOverlayView` bridging and camera tilt/rotation to work — without it, the map still
   * renders (flat, 2D) but stays a plain raster map. */
  readonly VITE_GOOGLE_MAPS_MAP_ID?: string
}

interface ImportMeta {
  readonly env: ImportMetaEnv
}
