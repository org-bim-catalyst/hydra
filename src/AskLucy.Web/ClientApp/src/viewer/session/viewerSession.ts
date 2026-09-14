/** The camera a recreated map should open at — carried across a theme toggle, a Map ID change,
 * and leaving/returning to the workspace route. */
export interface SessionCamera {
  latitude: number
  longitude: number
  zoom?: number
  heading?: number
  tilt?: number
}

/**
 * Viewer state that must outlive any single mount of `ViewerSurface`/`MapRenderTarget`.
 *
 * Both used to hold these in component refs, so navigating away from /studio (e.g. to /admin) and
 * back discarded them: the camera snapped back to its default, and — because `contentMode` lives
 * in a module-level store that DID survive — the remounted surface believed the map was still
 * loaded, skipped reloading it, and showed the placeholder for the rest of the session.
 *
 * Deliberately import-free, so `MapRenderTarget` can read it without pulling in the engine and
 * extension modules `resetViewerSession` needs.
 */
export const viewerSession: { mapContentId: string | null; camera: SessionCamera | null } = {
  mapContentId: null,
  camera: null,
}
