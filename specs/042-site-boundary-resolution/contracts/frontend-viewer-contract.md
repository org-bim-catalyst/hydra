# Contract: Frontend viewer integration

Three pieces, each with one clear responsibility, mirroring the existing `activeLocationStore` → `POIMarkerOverlay` → `GoogleMapsGisLayer` chain.

## 1. `activeSiteBoundaryStore.ts` (Zustand) — state

```ts
interface ActiveSiteBoundaryState {
  siteName: string | null
  centroid: { latitude: number; longitude: number } | null
  polygon: { latitude: number; longitude: number }[] | null   // exterior ring, closed; null when no active boundary
  confidence: number | null
  confidenceLevel: 'low' | 'medium' | 'high' | null
  source: 'osm-boundary' | 'manual-fallback' | 'government-cadastral' | 'ai-interpretation' | 'uploaded-boundary' | null
  sourceDetail: string | null
  alternativeCandidateNames: string[]
  setBoundary(result: ActiveSiteBoundary): void   // wholesale replace — never a partial merge
  clearBoundary(): void                            // edge case: a new, unrelated site is referenced
}
```

- Populated by the `__SITE_BOUNDARY__` SSE trailing event, exactly the way `activeLocationStore` is populated by `__LOCATION__` today — see `chat-pipeline-integration.md`, the primary mechanism. **Not** populated directly from an `IAgentTool` result — `site-boundary-resolver-tool.md` is a secondary surface for custom agents, not this store's data source.
- `clearBoundary()` is called whenever a *new* site reference resolves, before `setBoundary()` runs for the new one — satisfies the edge case "previously displayed boundary must be replaced, not left overlaid."

## 2. `SiteBoundaryOverlay.tsx` — React glue (imperative, `return null`)

Follows `POIMarkerOverlay.tsx`'s exact idiom:

```tsx
export function SiteBoundaryOverlay() {
  const handle = useGoogleMapsGisLayerHandle()      // however GoogleMapsGisLayerHandle is currently exposed to React (existing pattern, not introduced here)
  const polygon = useActiveSiteBoundaryStore((s) => s.polygon)
  const confidenceLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)

  useEffect(() => {
    if (!handle) return
    if (!polygon || !confidenceLevel) {
      handle.setSiteBoundary(null)
      return
    }
    handle.setSiteBoundary({ exteriorRing: polygon, confidenceLevel })
    return () => handle.setSiteBoundary(null)
  }, [handle, polygon, confidenceLevel])

  return null
}
```

- No DOM/JSX output — same as `POIMarkerOverlay`. All rendering happens inside the map's own Three.js scene via the handle.
- Also renders the confidence/source badge described in architecture doc §9.4 — a small, separate, purely-presentational React component (not imperative), positioned via the same map-projection utilities the viewer already uses for any screen-anchored UI (exact anchoring mechanism is an implementation detail for `tasks.md`, not fixed here).

## 3. `GoogleMapsGisLayerHandle.setSiteBoundary()` — the one additive change to existing code

```ts
export interface GoogleMapsGisLayerHandle {
  // ...existing members unchanged...
  /** spec 042: shows/updates/clears the animated site-boundary highlight. Pass null to remove it. */
  setSiteBoundary(input: { exteriorRing: GeoPointLike[]; confidenceLevel: 'low' | 'medium' | 'high' } | null): void
}
```

- Implemented inside `GoogleMapsGisLayer.ts`: stores the latest `input` in a closure variable; on the next `onDraw` (and every subsequent frame while non-null), positions a `THREE.Group` at `transformer.fromLatLngAltitude(centroid)` and (re)builds its contents via `SiteBoundaryRenderer.ts` only when the polygon/confidence actually changes (not every frame) — the animated comet segments still need per-frame updates for motion, handled inside `AnimatedBorderHighlight.ts`'s own `update(deltaSeconds)` called from the same `onDraw`.
- This is the **only** modification to `GoogleMapsGisLayer.ts` — no change to its existing camera/rotation/marker responsibilities.

## 4. `AnimatedBorderHighlight.ts` — the generalized shader effect

```ts
export interface AnimatedBorderHighlight {
  object3D: THREE.Object3D              // add this to the boundary Group
  update(deltaSeconds: number): void    // advances the comet(s) along the perimeter
  setConfidenceLevel(level: 'low' | 'medium' | 'high'): void   // modulates glow intensity/comet presence, no rebuild needed
  dispose(): void
}

export function createAnimatedBorderHighlight(
  localMeterPoints: { x: number; y: number }[],   // closed ring, in meters relative to the group's own origin (the centroid)
  confidenceLevel: 'low' | 'medium' | 'high',
): AnimatedBorderHighlight
```

- Takes **any** ordered point list — not hardcoded to a rectangle like the `BORDER_HIGHLIGHT.html` reference — satisfying the "keep it modular so it can be reused for other projects" requirement at the rendering layer too.
- `confidenceLevel` behavior (architecture doc §9.3): `high` → full comet animation + brightest additive glow; `medium` → dimmer/slower comets; `low` → static dashed/muted perimeter line only, no comets — reinforcing FR-006 visually, not just textually.
