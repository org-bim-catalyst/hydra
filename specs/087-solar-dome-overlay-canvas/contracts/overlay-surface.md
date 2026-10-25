# Contract: Overlay Surface and Drawing Space

**Feature**: specs/087-solar-dome-overlay-canvas

Three internal seams change. Only the first is visible to a capability; the other two are
viewer-internal and exist so the behaviour is testable.

---

## 1. `DrawingSpaceHandle` (capability-visible)

```ts
export interface DrawingSpaceHandle {
  /** The interleaved group — the map's own render pass (unchanged). */
  readonly group: THREE.Group
  /** NEW — the viewer-owned overlay group, drawn on the transparent surface above the map.
   * Parented inside the overlay scene when one exists; empty and unattached otherwise. */
  readonly overlayGroup: THREE.Group
  invalidate(): void
  onFrame(callback: (deltaSeconds: number) => void): void
  declareDrawingRequirement(requirement: 'shadows' | 'toneMapping'): void
}
```

**Guarantees**

- `overlayGroup` belongs to the calling extension alone. No call takes another extension's id,
  handle or group, and the handle carries no renderer, camera or scene reference.
- Content added to `overlayGroup` is drawn **above** the map (buildings included) and is registered
  to the map by the viewer's per-frame camera derivation.
- Adding the same object to both groups is the capability's own error; the registry never mirrors
  content between them.
- `onFrame` subscriptions and `invalidate()` are shared by both groups — there is still exactly one
  draw cadence.
- `release()` (framework-owned) removes and disposes both groups.

**Capability obligations**

- Draw only into `group` and `overlayGroup`; never store or use a renderer, camera or scene.
- Toggle visibility of its overlay content itself (both groups, if it splits content).
- Do not assume `overlayGroup` is attached before the map's overlay context exists.

---

## 2. `DrawingSpaceRegistry.bindOverlay` (viewer-internal)

```ts
bindOverlay(scene: THREE.Scene | null): void
```

- Called once by `GoogleMapsGisLayer` when the overlay surface becomes live, and with `null` when
  it is lost, removed or disposed.
- Parents every already-acquired `overlayGroup` into `scene`, preserving acquisition order; with
  `null`, unparents every one and keeps them available for the next surface.
- Never touches the interleaved binding (`bind(scene)`), which is unchanged.

---

## 3. `overlaySurface` module (viewer-internal)

```ts
export interface OverlaySurface {
  readonly canvas: HTMLCanvasElement
  readonly renderer: THREE.WebGLRenderer
  /** Sets the drawing buffer to `width × height` device pixels; no-op when unchanged. */
  resize(width: number, height: number): void
  /** forceContextLoss + dispose + remove the canvas; idempotent. */
  dispose(): void
}

/** Throws if the renderer cannot be created (no WebGL, context limit) — the caller logs. */
export function createOverlaySurface(
  container: HTMLElement,
  createRenderer?: (canvas: HTMLCanvasElement) => THREE.WebGLRenderer,
): OverlaySurface
```

**Guarantees**

- Applies the canvas's positioning (`absolute; inset: 0; pointer-events: none`) and z-order itself;
  the caller does not style it.
- The renderer is configured here, once: `alpha: true`, `antialias: true`, pixel ratio 1,
  transparent clear colour, sRGB output, ACES tone mapping.
- `dispose()` is safe to call twice and leaves nothing in the DOM.

---

## 4. `overlayCamera` module (viewer-internal)

```ts
/** Copies the map transform onto the camera and resets its pose to the identity the map's local
 * ENU frame assumes. Pure — no Three.js renderer, no DOM. */
export function applyMapTransformToCamera(camera: THREE.Camera, matrix: ArrayLike<number>): void
```

**Guarantees**

- `camera.projectionMatrix` equals `matrix`; `projectionMatrixInverse` is its inverse.
- `camera.matrixWorld` and `matrixWorldInverse` are identity after the call, regardless of the
  camera's prior position/rotation/scale.
- The same function is used for the interleaved camera and the overlay camera, so the two cannot
  diverge.
