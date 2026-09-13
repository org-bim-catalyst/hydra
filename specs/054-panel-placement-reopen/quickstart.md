# Quickstart: Clear-Area Panel Placement & Reopen Tray

How to run and validate this feature end-to-end. Design detail lives in [data-model.md](./data-model.md) and [contracts/](./contracts/).

---

## Prerequisites

- Node dependencies installed in `src/AskLucy.Web/ClientApp` (`npm ci`)
- For the browser scenarios: the app running and a location set, so the viewer shows the map rather than the placeholder (`ViewerSurface` falls back to `PlaceholderRenderTarget` without one)

---

## Automated validation

Run from `src/AskLucy.Web/ClientApp`:

```bash
# Placement logic, store behaviour, dock UI, a11y
npx vitest run src/viewer/panels

# Type check (the root tsconfig uses project references — a bare `tsc --noEmit` checks nothing)
npx tsc -b --noEmit

# Lint
npx eslint src/viewer/panels src/components/workspace-shell src/features/solar
```

Then the full suite, because page-level tests carry their own assertions about components this feature touches:

```bash
npx vitest run
```

### Expected coverage

| Area | File | Proves |
|---|---|---|
| Grid packing | `layout/arrangement.test.ts` | A1-A3, A6-A9 — panels that fit get zero-overlap positions |
| Cascade fallback | `layout/arrangement.test.ts` | A4, A5, A10 — smallest panel frontmost when space runs out |
| Pinned panels | `layout/arrangement.test.ts` | D5 — a `manuallyPlaced` panel is an obstacle, not a placement target |
| Candidate slots | `layout/arrangement.test.ts` | S1-S5 — a panel never blocks itself; empty list when nothing is clear |
| DOM normalization | `layout/reservedRegions.test.ts` | C2-C4 — host-relative conversion, zero-rect rejection, no throw |
| Close → tray → reopen | `store/floatingPanelStore.test.ts` | FR-006, FR-009, FR-010, FR-011 — including the 10-entry cap and minimize exclusion |
| Dock rendering | `components/PanelDock.test.tsx` | FR-007, FR-008 — renders nothing when empty |
| Dock accessibility | `components/PanelDock.a11y.test.tsx` | §7/§10 — no axe violations, labelled keyboard-reachable controls |

---

## Manual validation (browser)

### Scenario 1 — Panels open clear of chrome (US1, FR-002, SC-002)

1. Open the studio view with a location set. Confirm the account/theme cluster, viewer-tool stack, and extension toolbar are all visible top-right.
2. Ask Lucy for something that opens a panel, or activate the solar analysis extension.
3. **Expect**: the panel is fully visible and does not sit under the top-right chrome. The extension toolbar sits at its natural corner — *not* pushed ~460px down the screen, since D9 removes that stopgap.

### Scenario 2 — Grid, then cascade (FR-005a, FR-005b, SC-006)

1. Open two or three panels.
2. **Expect**: they tile side by side with no overlap.
3. Keep opening panels until the screen is crowded.
4. **Expect**: the layout flips to an offset cascade rather than an overlapping grid, and the smallest panel is the one on top (FR-005c — most visible with solar analysis open, which contributes a 96×96 widget alongside a 400×300 figures panel).

### Scenario 3 — Manual placement is respected (FR-005d)

1. With two panels open, drag one to a corner you choose.
2. Open a third panel.
3. **Expect**: the panel you moved stays exactly where you put it, and the new panel flows around it.
4. Trigger the "arrange" action on the dock.
5. **Expect**: *now* everything re-flows, including the panel you had moved.

### Scenario 4 — Drag placeholder (FR-005f, FR-005g, SC-008)

1. With at least one free region on screen, start dragging a panel by its title bar.
2. Move the pointer over open space.
3. **Expect**: a ghost outline appears showing where the panel will land, updating with no perceptible lag as you move between free regions.
4. Move the pointer over chrome or another panel.
5. **Expect**: the ghost disappears.
6. Release over a ghost. **Expect**: the panel snaps into that slot. Release with no ghost showing. **Expect**: the panel stays exactly where you dropped it.

### Scenario 5 — Close and reopen (US2, FR-006 through FR-011, SC-003, SC-004)

1. Start a fresh session with nothing closed. **Expect**: no dock rail is visible (FR-008).
2. Open a panel, then close it with the × button.
3. **Expect**: the dock rail appears on the left edge with an entry for that panel.
4. Click the entry.
5. **Expect**: the panel reopens with its original title and content, placed by the same clear-area logic — not necessarily where it was before — and the entry leaves the tray.
6. Minimize a different panel. **Expect**: it does *not* appear in the tray (FR-011); it stays on-screen as a compact bar with its own restore button.

### Scenario 6 — Resize (FR-005, SC-005)

1. With several panels open, resize the browser window smaller.
2. **Expect**: every panel stays fully within the viewport and none ends up newly buried under the top-right chrome.

### Scenario 7 — Stale association on reopen (FR-013)

1. Open a panel associated with viewer content (a panel carrying a "Locate in viewer" button).
2. Close it, change the active location so the associated layer is removed, then reopen it from the tray.
3. **Expect**: the panel reopens showing the invalid-association warning icon — not a silently broken "Locate" button.

---

## Verifying the stopgap removal (D9)

```bash
# Both should return nothing — the hardcoded dodge offsets are gone
grep -rn "xs: 460\|xs: 512" src/viewer/extensions/components/ExtensionToolbar.tsx \
  src/features/solar/components/CameraAttitudeWidget.tsx

# Both should match — they now declare themselves reserved instead
grep -rln "data-panel-reserved" src/viewer/extensions/components/ExtensionToolbar.tsx \
  src/features/solar/components/CameraAttitudeWidget.tsx
```

---

## Known limitation

Placement avoids UI the app knows about, not busy areas of the 3D scene itself. A panel may still land over a visually interesting part of the map — judging scene content at the pixel level is explicitly out of scope (spec Assumptions).

## Actual outcomes (T025, automated)

Verified 2026-09-13 via `npx vitest run` (full suite), `npx tsc -b --noEmit`, and `npx eslint`:

- **1272/1272 tests pass across 223 files** — including the 3 (`ChatPage.a11y.test.tsx`,
  `ChatPage.test.tsx`, `WorkflowDesignerPage.a11y.test.tsx`) that timed out in a pre-change baseline
  run of the same suite; re-running the full suite after every spec 054 change landed shows all
  three passing, confirming that was pre-existing environment flakiness (slow-machine timing), not
  a regression this feature introduced.
- Typecheck and lint are clean on every touched file (two pre-existing lint warnings remain, in
  `features/solar/panels/BuildingCorrectionsPanel.tsx` and `SolarTimeControlPanel.tsx` — files this
  feature did not touch).
- A real latent bug was found and fixed by T023's own regression test: `openPanel` previously
  always seeded a fresh panel's `contextStatus` as `'current'` when a `contextAssociation` was
  supplied, without checking whether the referenced layer still existed. A panel closed while its
  association was valid, whose layer was then removed while it sat in the tray, would reopen
  showing `'current'` instead of `'invalid'` — silently stale. Fixed via `initialContextStatus` in
  `floatingPanelStore.ts`, which checks `useViewerEngineStore`'s current layer list at panel-creation
  time rather than only reacting to a future `layerRemoved` event.

## Actual outcomes (T026, manual — NOT executed this session)

This development session has no running dev server or browser to drive Scenarios 1-7 live against.
**T026 is left unchecked in `tasks.md`** rather than claimed complete — the 7 scenarios above still
need a real pass in a running app (`https://localhost:7170/studio` or equivalent) before this
feature is considered fully validated end-to-end, per the constitution's Definition of Done (§19).
Everything each scenario exercises has automated coverage (see the "Expected coverage" table above)
proving the underlying logic is correct; what remains unverified is purely visual/interactive
polish — actual on-screen spacing, real drag-and-drop feel, and whether the dock's left-edge
placement reads well against the live map.
