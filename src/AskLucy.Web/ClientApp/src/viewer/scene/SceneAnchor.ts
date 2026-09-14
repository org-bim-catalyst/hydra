/** data-model.md "Reference Point" (spec FR-008) — the one real-world position all drawn content
 * is positioned from. */
export interface ReferencePoint {
  latitude: number
  longitude: number
}

type AnchorListener = () => void

/** research D2 — owns the viewer's single reference point. No capability may call `set()`; every
 * capability reads it indirectly through `api/coordinateFrame.ts`'s `worldToLocal`/`localToWorld`
 * — this is what makes "no capability may set its own" (FR-008) true by construction rather than
 * by convention. Fixes a real bug: `GoogleMapsGisLayer.ts` previously re-anchored its own private
 * `sceneAnchor` to a site boundary's centroid on every `setSiteBoundary()` call — a second,
 * capability-specific reference point living inside one layer file. This class is now the only
 * place a reference point is held.
 *
 * Set by the engine on first content load, and moved by the viewer itself whenever the active
 * location changes (`viewer/session/anchorFollowsActiveLocation.ts`). Found live (2026-09-14): it
 * used to be set exactly once, to wherever the map first opened — the device's own location — and
 * never moved when Lucy flew to a named site. Everything drawn relative to it (the sun-path dome,
 * the site-boundary ring) was therefore placed near the startup location, kilometres from the site
 * on screen, and the flat-earth conversion's error grew with that distance. Geometry positioned
 * from the anchor must rebuild when `version` changes — subscribe to learn when. */
class SceneAnchor {
  private referencePoint: ReferencePoint | null = null
  private currentVersion = 0
  private readonly listeners = new Set<AnchorListener>()

  get(): ReferencePoint | null {
    return this.referencePoint
  }

  /** Increments every time the reference point actually moves. */
  get version(): number {
    return this.currentVersion
  }

  /** A no-op when the point is unchanged, so repeated sets never trigger needless rebuilds. */
  set(point: ReferencePoint): void {
    const current = this.referencePoint
    if (current && current.latitude === point.latitude && current.longitude === point.longitude) return

    this.referencePoint = point
    this.currentVersion += 1
    for (const listener of this.listeners) listener()
  }

  /** Arrow property so it can be handed to `useSyncExternalStore` unbound. */
  subscribe = (listener: AnchorListener): (() => void) => {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }
}

/** Single module-level instance, mirroring every other viewer singleton's convention
 * (`viewerEngineInstance.ts`, `viewerExtensionRegistry`). */
export const sceneAnchor = new SceneAnchor()
