# Research: Solar Dome on Its Own Overlay Surface

**Feature**: specs/087-solar-dome-overlay-canvas | **Date**: 2026-10-25

Every decision below records what was chosen, why, and what was rejected, in the house style of
specs/052, specs/086 and specs/087's predecessor fixes. The root cause is taken as established
(production evidence, three shipped fixes) and is not re-investigated here.

---

## D1 — A second renderer on its own transparent canvas, owned by the map bridge

**Decision**: on `overlay.onContextRestored` the bridge creates a `<canvas>` appended to the map
container, a `THREE.WebGLRenderer` bound to it (its own WebGL context — no `context:` option), an
`overlayScene` and an `overlayCamera`. `onDraw` renders the interleaved pass exactly as today and
then renders `overlayScene` into the overlay surface.

**Rationale**: the map's building pass runs **after** `onDraw` returns, inside Maps' own context.
No object in our scene — regardless of `renderOrder`, material flags or depth manipulation — can
outrank a pass that executes later. Only a surface composited above the map's canvas escapes that
ordering. Keeping the surface next to the existing renderer honours specs/052 FR-038: renderer
creation and settings stay viewer-owned, capabilities never see a renderer.

**Alternatives considered**:

- *A CSS-transformed sprite/DOM/SVG rendering of the dome.* Rejected: throws away the entire
  Three.js dome (tubes, textures, depth ordering) and the camera math that already works.
- *`preserveDrawingBuffer`/post-composite tricks on the map canvas.* Rejected: touching Maps' own
  canvas is unsupported and would fight its renderer.
- *A second `WebGLOverlayView`.* Rejected: it is still drawn inside the map's pass, before the
  buildings — the bug is unchanged.

---

## D2 — The capability seam: `DrawingSpaceHandle.overlayGroup`

**Decision**: `DrawingSpaceRegistry.acquire()` gives each handle a second, empty `THREE.Group`
(`overlayGroup`) in addition to `group`. A new `bindOverlay(scene: THREE.Scene | null)` parents
every acquired `overlayGroup` into the overlay scene (replaying groups acquired before the map's
context existed) and unparents them when passed `null`. `release()` removes and disposes both
groups.

**Rationale**: the framework already hands a capability exactly one thing it may draw into, and
its handle carries no renderer/camera/scene. Extending that same handle with a second owned group
adds the capability the dome needs without inventing a second registry or a second context
method, and keeps the T036 isolation property: an extension still has no way to name another
extension or reach shared state. The overlay scene itself is bound by the bridge (viewer-owned),
never by a capability.

**Alternatives considered**:

- *`acquireOverlaySpace()` on `ExtensionContext`.* Rejected: a second handle type and a second
  release path through `withdrawContributions`, for one consumer.
- *The solar feature creating its own canvas.* Rejected: violates FR-037/FR-038 — a capability
  owning a renderer and a surface.
- *Moving groups between scenes in `SolarScene` via a scene reference.* Rejected: a capability
  reaching the shared scene, which is exactly what the drawing-space seam exists to prevent.

---

## D3 — Camera: the map transformer's matrix, copied each frame; pose is identity

**Decision**: in `onDraw`, after the existing interleaved transform is read, call a pure helper
`applyMapTransformToCamera(overlayCamera, matrix)` which:

1. copies the matrix into `camera.projectionMatrix` and its inverse into `projectionMatrixInverse`;
2. resets the camera pose to the origin with identity rotation and unit scale;
3. updates `matrixWorld` / `matrixWorldInverse`.

**Rationale**: the interleaved camera today sets **only** `projectionMatrix` from
`transformer.fromLatLngAltitude({lat, lng, altitude: 0})` at the scene anchor and leaves the
camera at the origin with identity orientation, so `matrixWorldInverse` is identity and the
projection matrix alone maps the ENU scene into clip space. The overlay must reproduce that
exactly, or the dome will not register with the map. Resetting the pose in the helper makes "copy
the pose" explicit and testable instead of an accident of construction defaults.

**Alternatives considered**:

- *Compute a separate camera from map center/zoom/heading/tilt.* Rejected: a hand-rolled
  projection will drift from Google's own (tilt is not a plain perspective parameter), and the
  transformer is the supported source of truth.
- *Share the interleaved camera object.* Rejected: the interleaved renderer must keep its own
  camera; a shared object makes one surface's state leak into the other.

---

## D4 — Sizing, pixel ratio and pointer input

**Decision**: the canvas is `position: absolute; inset: 0; pointer-events: none` with a z-index
above the map's canvas, appended to the same container the map is constructed in. Its drawing
buffer is resized to the map canvas's `width`/`height` (device pixels) in the same block of
`onDraw` that already tracks `lastCanvasWidth`/`lastCanvasHeight`; `setPixelRatio(1)` and
`setSize(width, height, false)` mirror the interleaved renderer.

**Rationale**: the map container is the full-size positioned element (`position:absolute; inset:0`
in `MapRenderTarget`), and Maps sizes its own canvas to that box. `inset: 0` therefore matches the
map canvas's CSS box without reading or copying styles that Maps owns, and it follows container
resizes for free. Device pixels follow the map canvas because the interleaved path already
derives them from `gl.canvas.width/height`. The `false` third argument keeps Three from writing
CSS size onto a canvas whose CSS box is owned by the positioning rule.

**Alternatives considered**:

- *Cloning the map canvas's inline `width`/`height` styles.* Rejected: Maps owns those; copying
  them creates two writers for the same box.
- *ResizeObserver on the container.* Rejected: the bridge already has a per-draw size check that
  costs two integer comparisons; a second observation mechanism is redundant.
- *`pointer-events: auto` on the canvas.* Rejected: the dome is not interactive; the map must keep
  every gesture.

---

## D5 — Overlay renderer settings: transparent, antialiased, same colour treatment, no shadows

**Decision**: `new THREE.WebGLRenderer({ canvas, alpha: true, antialias: true })`,
`setPixelRatio(1)`, `setClearColor(0x000000, 0)` with default `autoClear` so every frame clears
colour+depth+stencil to transparent black; `outputColorSpace = SRGBColorSpace` and
`toneMapping = ACESFilmicToneMapping`, mirroring the interleaved renderer so the dome's lit
materials (`MeshStandardMaterial` dial and shell) keep their present appearance. The overlay scene
gets its own `AmbientLight(0xffffff, 1)` — the only light the dome needs. The overlay renderer is
**not** bound to `rendererState` and gets no shadow map: the dome casts and receives nothing the
analysis reports on.

**Rationale**: the dome's shell and dial are lit materials; the interleaved scene lights them with
one ambient light. Without the same light on the overlay scene they would render black. Matching
the colour-space and tone-mapping treatment is what keeps "the dome looks the same as it does
today", which the spec requires besides occlusion. Antialiasing matches what the dome gets from
the map's own context today; the geometry is sparse, so the cost is small. Keeping the overlay out
of `rendererState` preserves FR-038's meaning: the declared requirements describe the shared
renderer.

**Alternatives considered**:

- *`alpha: false`.* Rejected: the surface must be transparent over the map.
- *Default tone mapping (none).* Rejected: the dial and shell would visibly shift colour against
  today's render.
- *Sharing `rendererState`.* Rejected: a capability's declared 'shadows' requirement has no
  meaning for a surface that has no shadow-casting light.

---

## D6 — Lifecycle: created with the overlay context, disposed on loss, removal and teardown

**Decision**: the surface is created in `onContextRestored` inside a `try/catch` (a context limit
or a driver refusal is logged, not thrown) — disposing any surface left from a previous restore
first, since the callback can fire more than once. It is disposed by one helper that calls
`forceContextLoss()`, `dispose()` and removes the canvas. `overlay.onContextLost`, `overlay.onRemove`
and the handle's `dispose()` all call it, and each also calls
`drawingSpaceRegistry.bindOverlay(null)` so no capability keeps drawing into a dead scene.

**Rationale**: a Three renderer's `dispose()` releases GPU resources but not the WebGL context
itself; the browser caps live contexts, and this surface is created and destroyed on every map
recreation (theme toggle, Map ID change, route change). `forceContextLoss()` is what actually
returns the context. `onContextLost` is also Maps' own signal that the shared context died, so it
is the correct place to stop drawing rather than waiting for a draw against an invalid context.

**Alternatives considered**:

- *`dispose()` only.* Rejected: leaves the context alive until GC; repeated map recreations are
  exactly the pattern that exhausts a context budget.
- *Relying solely on `overlay.onRemove`.* Rejected: `onContextLost` can occur without removal, and
  a lost context drawing a frame is a guaranteed console error storm.

---

## D7 — Delete the workarounds; normal depth sorting inside the dome

**Decision**:

- `buildDomeOccluder` and `FixedFurnitureObjects.occluder` are deleted, along with the near-plane
  shader.
- The fixed/dated groups lose their `renderOrder` 10/11 — they keep acquisition order only.
- `buildTubeFromPoints` keeps `depthWrite` **only when opaque** (`opacity >= 1`) and uses
  `depthTest: true`; the shell keeps `depthWrite: false` and `depthTest: true`; the current-position
  marker becomes ordinary opaque geometry (`depthTest: true`, `depthWrite: true`).
- The shell keeps `renderOrder = 1` — an intra-dome ordering that makes the enclosing transparent
  shell draw after the dome's other transparent objects, unrelated to the map.
- The hour-mark sprite keeps `depthTest: false` — labels are meant to read through the dome's own
  geometry, and that is an instrument choice, not a map workaround.

**Rationale**: the three removed mechanisms existed only to beat a later pass. Inside the overlay
they are at best inert and at worst misleading — the occluder's near-plane depth would now punch
depth into the overlay's own buffer, which is the opposite of what is wanted. Normal depth sorting
gives the correct picture within the dome: opaque arcs write depth and occlude each other properly
rather than relying on draw order; transparent lines do not write depth, so they tint rather than
hole the geometry behind them; the marker sits in front of the arcs it stands on.

**Alternatives considered**:

- *Keep the occluder disabled "just in case".* Rejected: dead code carrying a misleading rationale.
- *Leave `depthWrite: false` on opaque tubes too.* Rejected: opaque geometry must write depth for
  normal sorting; without it, a farther arc drawn later paints over a nearer one — a real
  regression once the map is no longer doing that sorting for us.
- *Remove the sprite's `depthTest: false` too.* Rejected: the hour labels would be hidden by the
  lattice and shell they label.

---

## D8 — Failure containment: one `try/catch` per surface

**Decision**: keep the existing `try/catch` around the interleaved frame; wrap the overlay frame in
its **own** `try/catch` that logs
`[GoogleMapsGisLayer] Solar dome overlay render failed:`. Surface creation has its own `try/catch`
logging `[GoogleMapsGisLayer] Could not create the solar dome overlay surface:`.

**Rationale**: FR-008 requires that a failure on either surface cannot stop the other. A single
shared `try/catch` around both makes the order decide which one is skipped. Separate blocks cost
three lines and make the containment literal.

**Alternatives considered**:

- *One shared block.* Rejected: a dome fault would also skip the map's own three-dimensional
  content that frame, which today renders fine.
- *Rethrowing into Maps' callback.* Rejected: `WebGLOverlayView` swallows them (documented in the
  existing code), which is why this file logs rather than throws.

---

## D9 — Test seams

**Decision**:

- `viewer/scene/overlayCamera.ts` → `applyMapTransformToCamera(camera, matrix)`: pure, tested
  against a real `THREE.PerspectiveCamera` in jsdom.
- `viewer/layers/gis/overlaySurface.ts` → `createOverlaySurface(container, createRenderer?)` with
  an injectable renderer factory (default: `new THREE.WebGLRenderer(...)`), so lifecycle, sizing and
  disposal are tested with a fake in jsdom — where no WebGL context exists.
- `DrawingSpaceRegistry.test.ts` extended: distinct `overlayGroup`s per extension; `bindOverlay`
  replays already-acquired groups; `release` removes and disposes both groups; the T036 isolation
  key list grows by exactly `overlayGroup`.
- `features/solar/scene/SolarScene.overlay.test.ts`: dome groups are children of `overlayGroup` and
  absent from `group`; `buildingsGroup`/`groundGroup`/`lightGroup` are the reverse; the ground
  offset and visibility apply to both.
- `sunPathCurve.test.ts`: the occluder suite and the group-`renderOrder` suite are deleted; the
  depth suite is inverted to pin `depthTest: true` and opacity-dependent `depthWrite`.

**Rationale**: the bridge itself is not jsdom-testable (its own test file says so). Every new
behaviour is therefore pushed into one of three jsdom-safe seams rather than left unverified.

**Alternatives considered**:

- *Mocking `three` wholesale in a bridge test.* Rejected: high-churn mock of a large module for
  less fidelity than testing the extracted units.
- *Playwright-only verification.* Rejected: keep the unit-testable core honest; the live map is
  still covered by quickstart.
