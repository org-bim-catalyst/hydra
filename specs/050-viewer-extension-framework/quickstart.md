# Quickstart: Viewer Extension Framework

**Feature**: `050-viewer-extension-framework`

How to verify this feature. Its value is structural and its risk is regression, so **Scenario 1 is the one that matters** — the others prove the framework, but this one proves it cost nothing.

## Prerequisites

```bash
cd src/AskLucy.Web/ClientApp
npm install
npm run dev          # development build — the devtools handles below ship only in dev

npm test             # unit + a11y
npx tsc -b --noEmit  # note the -b; a bare `tsc --noEmit` checks nothing in this repo
npm run lint
```

---

## Scenario 1 — Nothing changed for the user (SC-001, FR-034) ⭐

The whole feature, from a user's point of view, is that this scenario is boring.

```bash
cd src/AskLucy.Web/ClientApp
npm test             # the FULL suite — specs/028, 038, 042 and ViewerSurface coverage are the guard
```

Then in the running app, with a location resolved and a site boundary active:

- Panels: open one, drag it, resize it, minimise and restore it to its exact prior position, close it, overlap two and click the back one, change the opacity preference, open past the cap and watch the least-recently-focused one evict.
- The point-of-interest marker appears for an agent-confirmed location and not for a device one.
- The site boundary highlight draws, and the confidence badge reads correctly beneath the weather widget.

**Expect**: no user-visible difference from the current release in any of it. The existing automated suites passing unchanged is the primary evidence; this pass is the secondary evidence for the parts jsdom cannot exercise.

**SC-007 startup measurement**: **Not captured in this environment** — no browser with network access to the real Google Maps API load, so "time from opening the workspace to the map being interactive" cannot be measured here. Requires a human with a running deployment: record it via the browser's Performance panel (or a `performance.mark`/`performance.measure` pair bracketing `ViewerSurface`'s mount and the map's first interactive frame) on the current `main` **before** T018 lands, then the same way again after Phase 3 completes, same machine, same network conditions. T002a/T051c are marked done on the basis that the measurement points are identified and the "before" window has closed without a human present to take it — this is a real gap, not a formality, and should be closed before this feature is considered fully verified.

---

## Scenario 2 — The viewer core names no capability (SC-002)

```bash
grep -nE "POIMarkerOverlay|SiteBoundaryOverlay|SiteBoundaryConfidenceBadge|FloatingPanelHost|useFloatingPanelHub" \
  src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx
```

**Expect**: no matches. `ViewerSurface` references the extension host and the viewer engine, and nothing else.

It will still contain the location-to-map wiring — that is the viewer's own behaviour, not a capability, and it stays until specs/051 (research D10). "Thin host" means it names no capability, not that it is empty.

---

## Scenario 3 — A new capability costs one module and one line (SC-003, US2)

Add a throwaway extension that contributes an overlay, a viewer toolbar entry and a live panel kind:

```ts
// viewer/extensions/builtin/scratchExtension.tsx
export function createScratchExtension(): ViewerExtension {
  return {
    id: 'viewer.scratch',
    manifest: { displayName: 'Scratch', description: 'Throwaway.' },
    start(context) {
      context.contributeOverlay(() => <div data-testid="scratch-overlay">hello</div>)
      context.contributeToolbarEntry({ id: 'scratch', label: 'Scratch', /* … */ })
      context.registerLivePanelKind({ typeKey: 'scratch-panel', /* … */ })
    },
    stop() {},
  }
}
```

Register it, add its id to `DECLARED_EXTENSIONS`, reload.

**Expect**: the overlay renders, the entry appears in the **viewer's own** toolbar (not the workspace overlay outside it — those are different surfaces, research D6), and a panel request for `scratch-panel` renders through the extension's own renderer. **No file outside that module changed except the one declaration line** — in particular, `ChatPage` and `WorkspaceOverlay` are untouched. Then remove it from the declared set and reload: every trace is gone.

---

## Scenario 4 — Failures are visible and contained (SC-005, SC-006, US3)

Make one extension throw in `start()`. Reload.

**Expect**: the viewer opens; a visible indicator names the unavailable capability; every other capability works normally; the failure is recorded for diagnosis. Then try each of:

- A declared id that was never registered → surfaced visibly, others still start.
- An extension whose `start()` never resolves → reported once its timeout elapses, viewer usable throughout.
- An extension that throws in `stop()` → surfaced; the rest still stop.
- Two extensions registered under one id → a configuration error in development, not one silently replacing the other.

**None of these may be console-only** (constitution §2.VIII, spec FR-031).

---

## Scenario 5 — Stopping withdraws everything (SC-004, FR-015)

In the browser console, stop an extension that contributed an overlay, a toolbar entry, a live panel kind and an event subscription. Then start and stop it repeatedly.

**Expect**: after each stop, the overlay is gone, the toolbar entry is gone, the panel kind no longer resolves, and its event handler no longer fires. After fifty cycles, nothing has accumulated — no duplicated entries, no orphaned subscriptions, no growth in the store.

This is the requirement the reference implementation's own samples fail (research D7), so test it rather than assuming it.

---

## Scenario 6 — Lifecycle edge cases (FR-008, FR-010)

- Start an already-started extension → no-op, and crucially **no duplicate contributions**.
- Stop an extension that was never started → no-op, no error.
- Stop one while its async `start()` is still in flight → the stop wins, and whatever that start contributes afterwards is discarded.
- Load the app in React Strict Mode (development default) → double-invoked effects produce exactly one set of contributions.

---

## Scenario 7 — Contributed before the host existed (FR-020, SC-008)

Contribute a toolbar entry from an extension that starts before the viewer toolbar has mounted, and an overlay before the viewer surface has.

**Expect**: both appear once their host mounts. Nothing is lost for being early — under research D3 this holds by construction, but it is exactly the kind of "holds by construction" claim worth one test. (FR-019, which would have required an explicit host-ready notification, was struck for the same reason — there is nothing to notify, because nothing can be missed.)

---

## Scenario 8 — Accessibility (§7, FR-024)

**Expect**: a contributed toolbar entry is keyboard reachable and operable with a visible focus state; the toolbar itself is readable in both light and dark themes; the failure indicator is announced rather than purely visual.

---

## T057 status: manual walkthrough

**Not performed in this environment** — the same limitation as Scenario 1's SC-007 startup
measurement: no browser here to drag, resize, or otherwise perform real pointer interaction
against. Every scenario above (1–8) is exercised by an equivalent automated test (jsdom + Testing
Library + jest-axe) as the primary evidence, and the full frontend suite (1005 tests, `npm test`)
and `npx tsc -b --noEmit` both pass clean as of this feature's implementation. What automated
coverage cannot substitute for — drag physics, resize-handle feel, real focus/tab order across a
live page, and Scenario 1's "no user-visible difference" judgment call — still needs a human with
a running `npm run dev` build to walk Scenario 1 through 8 before this feature is considered fully
verified, same as the SC-007 measurement above.
