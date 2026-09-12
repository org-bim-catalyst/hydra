# Contract: Coordinate Frame and Drawing Space

**Feature**: `051-viewer-scene-content-api`

The two guarantees every drawing capability is built on (research D2, D3, D4). Getting either
wrong here is expensive to undo later — this is why this feature exists ahead of specs/052.

## Coordinate Frame (`api/coordinateFrame.ts`)

```ts
/** ENU (East-North-Up) — the ONE local positioning convention every capability draws in.
 * X = metres East of the viewer's single reference point.
 * Y = metres North of the viewer's single reference point.
 * Z = metres above ground.
 * Maps directly onto this scene's own Three.js axes with no remapping (confirmed against the
 * existing SiteBoundaryRenderer/AnimatedBorderHighlight code, which already places a local
 * {x,y} point straight into new THREE.Vector3(x, y, 0)) — a property of how the map bridge's
 * camera matrix sets up its local tangent plane, not something a capability needs to know. */
interface LocalPosition {
  x: number
  y: number
  z: number
}

function worldToLocal(point: { latitude: number; longitude: number }, altitudeMetres: number): LocalPosition
function localToWorld(position: LocalPosition): { latitude: number; longitude: number; altitudeMetres: number }
```

- There is exactly one reference point at a time (FR-008) — `getReferencePoint()` on the engine
  reads it; nothing but `SceneAnchor` itself ever sets it.
- `worldToLocal`/`localToWorld` are pure functions of the current reference point — calling them
  again after the reference point changes (FR-012) produces correct, non-stale results; nothing
  needs to be re-anchored manually by a capability.
- Precision: accurate to within 1 metre at the working scale this feature targets (SC-002) — the
  same tolerance `GoogleMapsGisLayer`'s existing equirectangular approximation already delivers at
  typical site distances, not unlimited-precision geodesy (spec Assumptions: "the conversion's
  accuracy limits are stated rather than assumed unlimited").

## Drawing Space (`scene/DrawingSpaceRegistry.ts`)

```ts
interface DrawingSpaceHandle {
  readonly group: THREE.Group
  invalidate(): void
  onFrame(callback: (deltaSeconds: number) => void): void
  declareDrawingRequirement(requirement: 'shadows' | 'toneMapping'): void
}

function acquire(extensionId: string): DrawingSpaceHandle
function release(extensionId: string): void   // removes the group from the scene, disposes every
                                                 // descendant's geometry/material/texture, clears
                                                 // every onFrame subscription
```

**What a capability may do**: add/remove/mutate objects under `handle.group` only. Position them
using `LocalPosition` values from `worldToLocal` (never a raw Three.js coordinate assumption).
Call `handle.invalidate()` to request a redraw. Call `handle.onFrame(...)` if it needs continuous
animation. Call `handle.declareDrawingRequirement(...)` if it needs a global renderer feature.

**What a capability may never do**: receive or hold a reference to the `THREE.Scene`, the active
`THREE.Camera`, or the `THREE.WebGLRenderer`. Call `WebGLOverlayView.requestRedraw()` directly.
Set `renderer.shadowMap.enabled`/`toneMapping`/`outputColorSpace` directly.

**Withdrawal is framework-owned, not author-owned** (FR-014, mirroring specs/050's
contracts/extension-context.md "Why tracking is framework-side"): `release()` is called
automatically when the owning extension stops, via a `drawingSpace` contribution recorded the same
way specs/050 already records `overlay`/`toolbarEntry`/`livePanelKind`/`eventSubscription`
contributions. An extension author never calls `release()` themselves and cannot forget to.

**A throwing `onFrame` callback is contained, not fatal** (FR-019, constitution §2.VIII
NON-NEGOTIABLE): the registry invokes every `onFrame` subscriber inside a try/catch. A thrown
callback is recorded and surfaced as a `drawingCallbackFailed` event — the same containment
posture specs/050 already built for tracked `on()` event handlers, applied here to the one other
place this codebase now invokes third-party callback code on a schedule it doesn't control.

**Draw order is acquisition order** (FR-015): groups are appended to the scene in the order their
`DrawingSpaceHandle` was acquired, and this order does not change when an unrelated extension
releases and later re-acquires its own space — the same insertion-order convention specs/050
already uses for toolbar entries, so there is one ordering rule in this codebase, not two.

## Redraw Scheduling (`scene/RedrawScheduler.ts`)

```ts
function invalidate(): void   // the only sanctioned redraw request, from any caller
```

- Multiple `invalidate()` calls before the next frame coalesce into exactly one
  `WebGLOverlayView.requestRedraw()` call (FR-021).
- No calls in a frame → no redraw (FR-022) — this is the property `GoogleMapsGisLayer.onDraw`'s
  current unconditional `overlay.requestRedraw()` call violates today, and this feature's fix.
- A capability that needs continuous redraws for an ongoing animation uses `onFrame` (which calls
  `invalidate()` on its own behalf each frame it's subscribed), not repeated manual `invalidate()`
  calls from a `setInterval`/`requestAnimationFrame` the capability drives itself — driving your
  own redraw loop is exactly what research D4 (specs/051) and the existing `GoogleMapsGisLayer`
  bug both get wrong today.
