# Quickstart: Viewer Scene and Content API

How to verify this feature. Like specs/050, its risk is regression against four already-shipped
capabilities plus the existing map bridge, so the automated suites are the primary evidence and
this walkthrough is secondary, for what jsdom cannot exercise.

## Prerequisites

```bash
cd src/AskLucy.Web/ClientApp
npm install
npm run dev

npm test
npx tsc -b --noEmit   # note the -b; a bare `tsc --noEmit` checks nothing in this repo
npm run lint
```

---

## Scenario 1 — The map still appears on startup, unasked (SC-001, FR-003)

Open the workspace with a resolvable location.

**Expect**: the map appears exactly as it does today, with no request from the user or Lucy.
Internally it now loads via `engine.loadContent({ kind: 'gis', ... })` called from
`ViewerSurface`'s own startup effect rather than a raw `addLayer` call — no user-visible
difference.

---

## Scenario 2 — Lucy loads, replaces and clears content (SC-001, US1)

Ask Lucy to show a model at a known location; then ask her to show something else; then ask her to
clear it.

**Expect**: the model loads (a loading indicator appears first — FR-005), then is replaced when a
different content item is requested (the previous content's Three.js resources are released —
verified by `listContent()` no longer naming the old id, and by the fifty-cycle resource test in
`RedrawScheduler`/`DrawingSpaceRegistry`'s own suites for the structural guarantee), then is
cleared, returning the viewer to the map.

---

## Scenario 3 — Content positions correctly and stays correct (SC-002, US2)

Load content at a known real-world coordinate. Pan, tilt and zoom the camera. Load a second piece
of content far from the first, forcing the reference point (if it moves) to be exercised.

**Expect**: content appears at the correct location and stays correctly placed through camera
movement. If the reference point changes, both old and new content remain correctly positioned —
this is `worldToLocal`/`localToWorld` being pure functions of the current reference point
(research D2), not a manual per-capability re-anchor.

**Automated equivalent**: `SceneAnchor.test.ts` asserts `worldToLocal` round-trips through
`localToWorld` within the stated tolerance, before and after `SceneAnchor`'s reference point is
changed.

---

## Scenario 4 — Two drawing capabilities can't disturb each other (SC-003, US2)

Start two extensions that each `acquireDrawingSpace()` and draw a distinct, identifiable shape.
Attempt (from a test, not a real capability) to reach into the other's group.

**Expect**: neither can see or mutate the other's `THREE.Group` contents — verified structurally in
`DrawingSpaceRegistry.test.ts` (each handle's `group` is a distinct `THREE.Object3D`, parented
under the one shared scene, with no shared references between them) and by inspection (neither
`DrawingSpaceHandle` nor `ExtensionContext` exposes any way to enumerate or reach another
extension's group).

---

## Scenario 5 — Redraw coalescing and idle behavior (SC-004, SC-006, US4)

In the browser console (development build), call `invalidate()` several times within one frame via
two different drawing spaces' handles.

**Expect**: exactly one underlying `requestRedraw()` call per frame, not one per `invalidate()`
call — verified directly in `RedrawScheduler.test.ts` with a stubbed `requestRedraw`. With nothing
subscribed via `onFrame` and no pending `invalidate()` calls, confirm (via the same stub) that no
redraw fires while idle.

**Frame-rate comparison (SC-004)**: **Not measured in this environment** — same limitation as
specs/050's SC-007: no GPU-capable browser here. The method is documented for a human to run: load
the same scene with one drawing capability active, note the frame rate (browser Performance panel),
then with several active, and compare. Record both figures here once available.

---

## Scenario 6 — Fifty-cycle resource accounting (SC-005, FR-033, FR-034)

Start and stop a drawing capability that acquires a Drawing Space, adds geometry, and declares a
drawing requirement, fifty times in a row (mirroring specs/050's T052 pattern, which caught a real
event-listener leak in that feature).

**Expect**: `DrawingSpaceRegistry`'s internal tracking (or, structurally, the scene graph's child
count and any geometry/material dispose call counts) return to their starting values after every
cycle — asserted directly in `DrawingSpaceRegistry.test.ts`, not assumed.

---

## Scenario 7 — Element selection and framing (SC-007, US3)

Load content carrying element information. Select an element in the viewer; confirm its identity
and properties are readable via `getElementInfo`. Separately, present a panel referencing an
element and activate it.

**Expect**: `getElementInfo` returns the element's properties; activating the panel reference calls
`selectAndFrame`, which selects the element and re-frames the camera on it. Activating a reference
to an element that no longer exists returns a failed result with a stated reason (US3 AC4), not a
silent no-op. Content with no element information reports `hasProperties: false` on selection
(US3 AC5), not an empty property list.

---

## Scenario 8 — Every content failure explains itself (SC-008, US5)

Attempt to load an unsupported format, an unreachable/corrupt source, and content with no
placement, in turn.

**Expect**: each produces a distinct, understandable explanation through `ExtensionFailureNotice`
(research D9) — the format-unsupported case names the format; the unreachable case is recorded for
diagnosis (constitution §2.VIII); the unplaceable case is explicit rather than the content
appearing somewhere arbitrary. Already-displayed content remains displayed throughout.

---

## Scenario 9 — The one-time renderer color/lighting review (SC-009)

**Not performed in this environment** — same limitation as Scenario 5's frame-rate comparison.
The code change (research D5: `renderer.outputColorSpace = THREE.SRGBColorSpace`,
`renderer.toneMapping = THREE.ACESFilmicToneMapping`, set once in `onContextRestored`) is made and
the specific colors needing comparison are named in research.md D5
(`SiteBoundaryRenderer`'s `#9C62DE`/`#757575` ring and comet colors). A human with a running build
must capture a before/after screenshot of the site-boundary highlight (the one existing
Three.js-drawn visual in this scene) under both the old undeclared pipeline and the new one, and
record the comparison here, before this feature is considered fully verified — the same honesty
pattern specs/050 used for its own unmeasurable scenarios.

---

## Scenario 10 — specs/050's four migrated capabilities are unaffected (SC-010, FR-039)

```bash
npm test   # the FULL suite — panels, POI marker, boundary confidence, site boundary all included
```

**Expect**: every existing suite for the four capabilities specs/050 migrated passes unchanged,
despite `GoogleMapsGisLayer.ts`'s internal changes (reference-point ownership moving to
`SceneAnchor`, redraw routing through `RedrawScheduler`, the renderer-state change). This is the
regression guard this feature's own research.md D10 names as the load-bearing evidence, alongside
`ViewerEngine.contract.test.ts` passing unchanged for "no existing command changed meaning."
