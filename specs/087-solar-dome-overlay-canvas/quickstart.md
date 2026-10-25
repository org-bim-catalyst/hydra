# Quickstart: Verifying Spec 087

**Feature**: `087-solar-dome-overlay-canvas` | **Date**: 2026-10-25

How to run the feature and confirm each requirement holds. Every check below is mechanical: it
names what to do and what to see, or the command to run and the value it must print.

---

## Prerequisites

- From `src/AskLucy.Web/ClientApp`:
  - Type-check: `npx tsc -b --noEmit` (a bare `tsc --noEmit` checks nothing in this repo — the
    root tsconfig uses project references).
  - Tests: `npx vitest run src/viewer/scene src/viewer/layers/gis src/features/solar`
  - Lint: `npm run lint`
- To see it: run the app, open a site whose surroundings include tall buildings, activate **Solar
  Analysis** from the viewer toolbar. The dome draws around the site building.
- The map must be **vector** (a Map ID is configured). On a raster/flat map neither WebGL overlay
  nor dome has ever existed, and this spec changes nothing there.

---

## Check 1 — buildings can no longer cover the dome (FR-001 … FR-003, SC-001)

**What to do**: set the camera to isometric, zoom in until the surrounding buildings are large on
screen, then rotate the heading through 360° in ~30° steps and tilt between the app's Plan and
Isometric limits at each step.

**Expected**: the dome — shell, dial, mount post, monthly lattice, hour lines, chosen-day arc,
both solstice arcs, hour marks and the sun marker — stays visible at every heading and tilt. Where
a building stands between the camera and the dome, the dome draws over it.

**Not expected**: any building silhouette cutting an arc, the shell, a label or the marker; any
flicker as the camera settles.

**Before this change**: the same sweep shows buildings painting over the dome (the production
behaviour that prompted this spec).

---

## Check 2 — shadows and massing are unchanged (FR-003, FR-016)

**What to do**: with the analysis open at mid-morning, toggle **Show building massing** and scrub
the time slider.

**Expected**: massing boxes, the shadow ground plane and the shadows behave exactly as before this
change. The camera-attitude widget and the time/figures/day-length panels read the same values.

**Automated**: the full existing solar suite passes unchanged except for the render-flag
assertions listed under Check 5.

---

## Check 3 — the dome stays registered to the map (FR-004, FR-005, SC-002)

**What to do**: note where the compass dial's "N" sits relative to a fixed map feature (a building
corner). Rotate the heading by 90°, zoom in two steps, then resize the browser window (and open
the devtools dock to force a second resize).

**Expected**: after each gesture the dial's "N" is on the same ground feature again, the dome is
the same size relative to the site, and nothing is stretched, offset or blurred. A screen capture
before and after a resize overlays with the dome in the same place.

**Automated**: `overlayCamera.test.ts` pins the projection copy and the identity pose;
`overlaySurface.test.ts` pins the drawing-buffer resize.

---

## Check 4 — the surface is transparent and not interactive (FR-004, FR-014)

**What to do**: close the analysis (toolbar toggle) and look at the map; then reopen it. Drag,
zoom, right-drag to rotate and click a building with the analysis open.

**Expected**: closed — the map shows no rectangle, tint, ghost or stale dome. Open — every map
gesture lands on the map exactly as it does with the analysis closed. Inspection of the DOM
(devtools → Elements) shows the overlay `<canvas>` with `pointer-events: none` and `inset: 0`, and
**exactly one** such canvas.

---

## Check 5 — the workarounds are gone and the dome sorts normally (FR-011, FR-012, SC-004)

**Automated**: from the repository root

```powershell
rg -n "buildDomeOccluder|renderOrder = (9|10|11)|depthTest: false" src/AskLucy.Web/ClientApp/src/features/solar
```

**Expected**: only the hour-mark sprite's `depthTest: false` is reported (an instrument choice:
labels read through the dome's own geometry). No occluder, no group render order, no material
depth-test opt-out for the shell, tubes or marker.

**What to see**: the sun marker draws in front of the arcs it stands on; the arcs occlude one
another by depth rather than by build order; the translucent shell tints what is behind it without
hiding it.

---

## Check 6 — failures are surfaced, not silent (FR-008, SC-006)

**What to do**: in the devtools console, run the page with WebGL context creation forced to fail
for the *second* context (e.g. temporarily make `HTMLCanvasElement.prototype.getContext` return
`null` for a `webgl2` request issued after the map has started, or set the browser's
`--disable-webgl` only for a scratch profile).

**Expected**: one `[GoogleMapsGisLayer] Could not create the solar dome overlay surface:` error.
The map still pans, zooms, rotates, renders its buildings and shadows, and shows the site
boundary. The analysis panels still open.

---

## Check 7 — nothing accumulates, nothing survives a teardown (FR-009, FR-014, SC-005)

**What to do**: open and close the analysis five times; toggle the app theme (which recreates the
whole map) twice; then open the analysis again. Count canvas elements per map container in
devtools (Elements → `canvas`).

**Expected**: exactly one overlay canvas while the analysis is open, none after it is closed, and
never two. No console warnings about too many WebGL contexts after the cycles.

**Also**: navigate away from the workspace to Chat and back. The dome returns at the same place,
the camera is where it was left, and the panels reopen as before (workspace-state rule).

---

## Check 8 — theme and hour-line re-tint (FR-013)

**What to do**: with the analysis open, toggle between light and dark themes.

**Expected**: the hour lines re-tint to the new theme's colour; the dome does not rebuild, move or
flash; the dial and shell brightness follow the theme exactly as they did before this change.

**Automated**: the existing re-tint test in `sunPathCurve.test.ts` passes unchanged.

---

## Check 9 — the full gate

From `src/AskLucy.Web/ClientApp`:

```powershell
npx tsc -b --noEmit
npm run lint
npx vitest run
```

**Expected**: zero type errors, zero lint errors, full suite green — including the three new test
files (`overlayCamera.test.ts`, `overlaySurface.test.ts`, `SolarScene.overlay.test.ts`) and the
three extended ones (`DrawingSpaceRegistry.test.ts`, `sunPathCurve.test.ts`,
`GoogleMapsGisLayer.test.ts` unchanged).
