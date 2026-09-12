# Phase 1 Data Model: Viewer Scene and Content API

**Feature**: `051-viewer-scene-content-api`

Extends specs/027's `RenderLayer`/`ViewerEvent`/`ViewerCommand` model and specs/050's
`Contribution` model. Nothing described here replaces an existing shape (FR-038/FR-039) — every
entry below is new.

---

## Viewer Content

Something Lucy or the viewer itself has asked to be loaded and shown (spec Key Entities). Wraps a
`RenderLayer` (D1) with the lifecycle `RenderLayer` alone cannot express.

```ts
type ContentLoadState = 'loading' | 'loaded' | 'failed'

interface ViewerContent {
  id: string
  layerId: string                 // the RenderLayer this content is backed by
  source: ContentSource
  placement: WorldPlacement | null   // null only for content that legitimately has none yet (loading)
  loadState: ContentLoadState
  /** Present only when loadState === 'failed' (FR-035) — one of a closed set of reasons, never a
   * raw thrown-error string, so the notice (D9, research.md) can word each case distinctly. */
  failureReason: ContentFailureReason | null
}

type ContentFailureReason =
  | 'unsupported-format'
  | 'unreachable-or-corrupt'
  | 'unplaceable'
```

**Validation rules**:
- `id` is unique among currently-tracked content (FR-002).
- A `loadContent` request for a format not in the supported set (D1's glTF, or the built-in `gis`
  kind) resolves to `loadState: 'failed'`, `failureReason: 'unsupported-format'` — never a thrown
  exception (FR-007).
- Content supplied with no placement information resolves to `failed`/`unplaceable` rather than
  being placed at an arbitrary default (FR-013).

## Content Source

Where content comes from — never an arbitrary external address (spec Assumptions).

```ts
type ContentSource =
  | { kind: 'gis'; provider: 'google-maps'; center: { latitude: number; longitude: number }; zoom?: number }
  | { kind: 'model'; format: 'gltf'; fileId: string }   // fileId resolves through the platform's
                                                          // existing signed-URL file access — never
                                                          // a caller-supplied URL (spec Assumptions)
```

## World Placement

How content sits in the world, relative to the one `Reference Point` (spec Key Entities).

```ts
interface WorldPlacement {
  latitude: number
  longitude: number
  heightMetres: number       // above ground, 0 = ground level
  orientationDegrees: number // rotation about the Up axis, 0 = north-facing
  scale: number               // 1 = authored scale
}
```

**Validation rules**: `latitude`/`longitude` bounds match the existing `zoomToLocation` validation
(-90..90, -180..180) — one validation rule, reused, not reinvented.

## Reference Point / Scene Anchor

The one real-world position all drawn content is positioned from (FR-008). Owned exclusively by
`scene/SceneAnchor.ts`.

```ts
interface ReferencePoint {
  latitude: number
  longitude: number
}
```

Only the engine sets this (on first content load, and never mid-session by any capability — spec
Out of Scope explicitly excludes multiple simultaneous reference points). Read via
`engine.getReferencePoint()`; consumed internally by `worldToLocal`/`localToWorld` (below).

## Local Positioning Space (ENU)

The published conversion every capability draws through (FR-009/FR-010). **Convention: X = East,
Y = North, Z = Up** — stated once here, referenced everywhere else, never restated.

```ts
interface LocalPosition {
  x: number  // metres east of the reference point
  y: number  // metres north of the reference point
  z: number  // metres above ground
}

// api/coordinateFrame.ts
function worldToLocal(point: { latitude: number; longitude: number }, altitudeMetres: number): LocalPosition
function localToWorld(position: LocalPosition): { latitude: number; longitude: number; altitudeMetres: number }
```

Built on `GoogleMapsGisLayer`'s existing `toLocalMeters` equirectangular-projection math (research
D2 — corrected during implementation from an earlier, unreachable `transformer.fromLatLngAltitude`
proposal) — one implementation, not a parallel one.

## Drawing Space

The isolated area of the scene one capability draws into (spec Key Entities, FR-014).

```ts
interface DrawingSpaceHandle {
  readonly group: THREE.Group   // the capability's own subtree — add/remove children here only
  invalidate(): void            // requests a redraw (D4) — the only sanctioned redraw path
  onFrame(callback: (deltaSeconds: number) => void): void   // per-frame subscription (FR-023)
  declareDrawingRequirement(requirement: DrawingRequirement): void   // FR-016
}

type DrawingRequirement = 'shadows' | 'toneMapping'
```

Issued by `scene/DrawingSpaceRegistry.acquire(extensionId)`; withdrawn (group removed from scene,
every descendant's geometry/material/texture `.dispose()`d, every `onFrame` subscription cleared)
by `release(extensionId)` — called automatically when the owning extension stops, via a new
`drawingSpace` contribution kind added to specs/050's `Contribution` union (additive; specs/050's
existing four contribution kinds are untouched).

## Camera State

Published, read-only snapshot of where the viewer is looking (FR-025).

```ts
interface CameraState {
  latitude: number
  longitude: number
  heading: number    // degrees, 0 = north
  tilt: number       // degrees from vertical
  zoom: number
}
```

Read via `engine.getCameraState()`; changes are announced via the `cameraChanged` event (FR-026,
research D6), not polled.

## Element

An addressable part of loaded content (spec Key Entities, FR-027/FR-028).

```ts
interface ElementProperties {
  hasProperties: boolean
  /** Present only when hasProperties is true — read from the content as supplied, never
   * platform-managed (spec Out of Scope). */
  properties: Record<string, unknown> | null
}
```

Selection continues to use specs/027's existing `SelectionState` (`selectedLayerId`/
`selectedElementId`) unchanged — this feature adds `getElementInfo`/`selectAndFrame` on top of it,
never a second selection model.

## Resource Tracking

Not a user-facing entity — an internal accounting `DrawingSpaceRegistry` and the content loaders
keep, so FR-033/FR-034/SC-005's "returns to baseline after fifty cycles" is verifiable rather than
assumed. Tracked per `DrawingSpaceHandle` (every `THREE.Object3D` added under its `group`) and per
`ViewerContent` (every geometry/material/texture its loader created) — disposed in full on
`release()`/`unloadContent()` respectively.

---

## Relationships

```
ReferencePoint (1) ──anchors──> LocalPositioningSpace (1) ──positions──> WorldPlacement (*)
ViewerContent (*) ──has a──> WorldPlacement (0..1) ──placed via──> LocalPositioningSpace
ViewerContent (*) ──backed by──> RenderLayer (1)   [specs/027]
ViewerContent (*) ──indexes──> Element (*) ──has──> ElementProperties (1)
Extension (*) [specs/050] ──acquires──> DrawingSpace (0..1)
DrawingSpace (*) ──declares──> DrawingRequirement (*) ──resolved onto──> the one WebGLRenderer
CameraState (1) ──published by──> the viewer engine, read by any capability
```

## State: Content Load Lifecycle

```
(loadContent request)
        │
        ▼
    loading ──format unsupported──────────> failed (unsupported-format)
        │
        ├──source unreachable/corrupt──────> failed (unreachable-or-corrupt)
        │
        ├──no placement supplied───────────> failed (unplaceable)
        │
        ▼
     loaded ──(replaceContent)──> loading (new content; old content's resources released first)
        │
        └──(unloadContent)──> [removed; resources released]
```
