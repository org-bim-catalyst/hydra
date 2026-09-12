/** data-model.md "Reference Point" (spec FR-008) — the one real-world position all drawn content
 * is positioned from. */
export interface ReferencePoint {
  latitude: number
  longitude: number
}

/** research D2 — owns the viewer's single reference point. Nothing but the engine itself (on
 * first content load) may call `set()`; every capability reads it indirectly through
 * `api/coordinateFrame.ts`'s `worldToLocal`/`localToWorld`, never by calling `set()` itself —
 * this is what makes "no capability may set its own" (FR-008) true by construction rather than by
 * convention. Fixes a real bug: `GoogleMapsGisLayer.ts` previously re-anchored its own private
 * `sceneAnchor` to a site boundary's centroid on every `setSiteBoundary()` call — a second,
 * capability-specific reference point living inside one layer file. This class is now the only
 * place a reference point is held. */
class SceneAnchor {
  private referencePoint: ReferencePoint | null = null

  get(): ReferencePoint | null {
    return this.referencePoint
  }

  set(point: ReferencePoint): void {
    this.referencePoint = point
  }
}

/** Single module-level instance, mirroring every other viewer singleton's convention
 * (`viewerEngineInstance.ts`, `viewerExtensionRegistry`). */
export const sceneAnchor = new SceneAnchor()
