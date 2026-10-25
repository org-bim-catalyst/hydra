# Data Model: Solar Dome on Its Own Overlay Surface

**Feature**: specs/087-solar-dome-overlay-canvas | **Date**: 2026-10-25

This feature adds no persisted data. The "model" is the runtime ownership and lifetime of the
rendering surfaces and the content each carries — the part that is easy to get wrong and that the
tests pin.

---

## Entity: Overlay Surface

The viewer-owned, transparent drawing surface composited above the map.

| Attribute | Type | Rules |
|---|---|---|
| `canvas` | `HTMLCanvasElement` | Child of the map container; `position:absolute; inset:0; pointer-events:none`; z-index above the map canvas. Removed on dispose. |
| `renderer` | `THREE.WebGLRenderer` | Own WebGL context (`alpha: true`, `antialias: true`); `setPixelRatio(1)`; clear colour transparent black; sRGB output + ACES tone mapping; **no** shadow map. |
| `scene` | `THREE.Scene` | Owned by the map bridge. Contains exactly one `AmbientLight` plus the overlay drawing spaces. |
| `camera` | `THREE.PerspectiveCamera` | Projection from the map transformer each frame; pose reset to origin/identity each frame. |
| `width`, `height` | device pixels | Always equal to the map canvas's `width`/`height`; re-synced when they change. |

**Invariants**

- At most one overlay surface exists per map.
- It exists only while the map's `WebGLOverlayView` context is alive (vector maps only).
- Nothing outside `GoogleMapsGisLayer` holds a reference to `renderer`, `scene` or `camera`.
- A failed creation leaves no canvas and no renderer behind.

**States**

```text
absent ──onContextRestored (ok)──▶ live ──onContextLost / onRemove / dispose()──▶ absent
   │                                  ▲
   └──onContextRestored (throws)──▶ absent (logged); a later restore may still succeed
```

---

## Entity: Overlay Drawing Space

The capability-facing seam: one `THREE.Group` per extension, parented inside the overlay scene.

| Attribute | Type | Rules |
|---|---|---|
| `extensionId` | key | One group per extension per surface, same id as its interleaved space. |
| `overlayGroup` | `THREE.Group` | Created empty at `acquire()`; parented to the overlay scene on `bindOverlay(scene)`; unparented and emptied when the surface is lost or the extension is released. |

**Invariants**

- The handle exposes no renderer, camera or scene — only groups, scheduling and requirements.
- An extension's overlay group is unreachable from any other extension's handle (extends the
  existing T036 isolation property).
- Interleaved and overlay groups share the extension's `onFrame` subscriptions and `invalidate()`.
- A group acquired before the overlay scene exists is replayed into it when it appears.

---

## Entity: Dome Content (ownership map)

| Content | Built by | Parent (after) | Parent (before) |
|---|---|---|---|
| Enclosing shell | `buildDomeShell` | `overlayGroup` → `sunPathFixedGroup` | `group` → `sunPathFixedGroup` |
| Compass dial | `buildCompassDial` | `sunPathFixedGroup` | same |
| Mount post | `buildMountPost` | `sunPathFixedGroup` | same |
| Monthly lattice | `buildMonthlyGridArcs` | `sunPathFixedGroup` | same |
| Hour lines | `buildHourLines` | `sunPathFixedGroup` | same |
| Chosen-day arc, extremes, hour marks, sun marker | `buildDatedPath` | `sunPathDatedGroup` | same |
| Building massing | `buildFootprintMeshes` | `buildingsGroup` (interleaved) | unchanged |
| Shadow ground | `ShadowGround` | `groundGroup` (interleaved) | unchanged |
| Sun light | `SunLight` | `lightGroup` (interleaved) | unchanged |
| Site-boundary highlight | `createSiteBoundaryRenderer` | scene root (interleaved) | unchanged |

**Rules**

- The dome groups receive the same anchor, ENU frame, radius and ground offset as before; only
  their parent changes.
- Visibility of the analysis applies to both parent groups together (`SolarScene.setVisible`).
- Disposal scope is unchanged: the fixed group's contents are rebuilt only on site/year/zone/radius
  change; the dated group's only on date/site/radius change. The registry releases both groups.

---

## Entity: Camera Transform

The per-frame derivation that keeps the dome registered to the map.

| Input | Source | Output |
|---|---|---|
| Map transform matrix | `transformer.fromLatLngAltitude({ lat, lng, altitude: 0 })` at the published scene anchor | Overlay camera `projectionMatrix` (+ inverse) |
| Pose | Fixed: origin, identity rotation, unit scale | `matrixWorld`, `matrixWorldInverse` |

**Validation**

- Given the same matrix and a camera that was moved/rotated, the camera's world matrix is identity
  after the call.
- `projectionMatrixInverse × projectionMatrix ≈ identity`.
- The overlay's camera state is recomputed every draw; no camera state is cached across frames.

---

## Validation Summary (requirement → check)

| Requirement | Check |
|---|---|
| FR-001, FR-002, FR-003 | Ownership test (`SolarScene.overlay.test.ts`) + quickstart Q1/Q2 |
| FR-004 | Quickstart resize/pixel-ratio scenario; `pointer-events` asserted in `overlaySurface.test.ts` |
| FR-005 | `overlayCamera.test.ts` |
| FR-006, FR-020 | `DrawingSpaceRegistry.test.ts` (groups only; no renderer in the handle) |
| FR-007 | Code review: single `onDraw` call site; no `requestAnimationFrame` added; existing scheduler untouched |
| FR-008 | `overlaySurface.test.ts` (creation failure propagates/ logs) + quickstart Q6 |
| FR-009 | `overlaySurface.test.ts` (forceContextLoss + dispose + canvas removal) |
| FR-010 | Existing dome radius/anchor tests unchanged and still passing |
| FR-011, FR-012 | `sunPathCurve.test.ts` (occluder gone; depth rules inverted) + code search |
| FR-013 | Existing hour-line re-tint test unchanged |
| FR-014 | `SolarScene.overlay.test.ts` (visibility applies to both groups) |
| FR-015 | Quickstart navigation scenario |
| FR-016 | Full frontend suite; no solar/solar-position/shadow test changes beyond the render flags |
| FR-017, FR-018, FR-019 | The three new test files |
