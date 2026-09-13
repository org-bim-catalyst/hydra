# Implementation Plan: Clear-Area Panel Placement & Reopen Tray

**Branch**: `054-panel-placement-reopen` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/054-panel-placement-reopen/spec.md`

## Summary

Floating panels currently open at a blind cascade offset (`CASCADE_START = {x:40,y:40}`, 32px steps) with no knowledge of anything else on screen, and closing one discards it permanently. This feature replaces blind cascade with an **arrangement engine**: a pure function that takes the host's bounds, a set of reserved (occupied) rectangles, and the open panels, and returns a position for each — packing them into a non-overlapping **grid** when they fit, falling back to a **cascade** with smaller-panels-in-front z-ordering when they don't. Reserved rectangles are collected from the live DOM via a marker attribute, which is what lets placement see chrome living in a *different React tree* (`WorkspaceOverlay`'s page-level toolbars) that no panel-side store could otherwise know about. Closing a panel now moves it to a bounded **reopen tray** rendered as a left-edge rail (`PanelDock`), which also hosts the explicit "arrange" action and registers itself as a reserved region so it never becomes the next collision.

Two fragile stopgaps added earlier this session (`ExtensionToolbar` and `CameraAttitudeWidget` hardcoded to `top: 460/512` to dodge `WorkspaceOverlay`) are **removed** by this feature — those components instead declare themselves reserved and return to their natural corner positions.

## Technical Context

**Language/Version**: TypeScript 5.x (strict), React 19

**Primary Dependencies**: MUI (theming/components), Zustand (`floatingPanelStore`), `react-rnd` (drag/resize, already in use), `@remixicon/react` (icons). **No new runtime dependency** — see research D1 for the evaluated-and-rejected library options.

**Storage**: In-memory Zustand store, session-scoped (no `persist` middleware — matches the existing `floatingPanelStore` convention)

**Testing**: Vitest + jsdom + React Testing Library + `jest-axe` (a11y), per `vite.config.ts` and the existing `*.a11y.test.tsx` convention

**Target Platform**: Browser SPA (`src/AskLucy.Web/ClientApp`), served by ASP.NET Core; desktop through mobile breakpoints

**Project Type**: Web application — **frontend only**. Zero backend changes: no API, no DB, no MediatR handler. Panel layout is pure client UI state.

**Performance Goals**: Arrangement recompute ≤ 5 ms for the capped 10 panels + ~8 reserved rects (runs on discrete events only — open/close/resize/arrange, never per frame). Drag-time placeholder lookup must be O(1) per pointer move — candidate slots are computed once at drag start, not per `onDrag`.

**Constraints**: Panel coordinates are relative to `FloatingPanelHost`'s own box, while `getBoundingClientRect()` is viewport-relative — the DOM adapter MUST normalize against the host rect (the same rect `clampToViewport` already uses). `getBoundingClientRect()` returns all-zeros in jsdom, so the *pure* arrangement logic must take plain rect data (fully unit-testable) and the thin DOM adapter is tested separately with stubbed rects.

**Scale/Scope**: ≤ 10 concurrently open panels (`MAX_CONCURRENT_PANELS`), ≤ 10 reopen-tray entries, ~8 reserved regions. ~6 new files, ~7 modified.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Article | Gate | Status |
|---|---|---|
| §2-I Dependency Rule | Frontend-only change; no backend layer is touched, so no dependency arrow can invert. | ✅ PASS |
| §2-II SOLID (SRP/OCP) | Arrangement math, DOM region collection, store state, and rendering are four separate units. The arrangement function is extended by *adding* a mode, not editing panel components. | ✅ PASS |
| §2-III DRY/KISS/YAGNI | One arrangement function serves opening, resizing, explicit arrange, *and* drag-placeholder lookup — no parallel implementations. No new dependency, no abstraction layer for hypothetical future layout modes. | ✅ PASS |
| §2-VI Separation of Concerns | Placement *policy* lives in a pure module; React components only render its output. No layout math inside `FloatingPanel.tsx`. | ✅ PASS |
| §2-VIII No Silent Failures | Arrangement is synchronous and total — it always returns a position (FR-004), never throws or no-ops. No async path exists, so there is no promise to leave unhandled. Reopening a panel whose live-panel kind vanished surfaces the existing `unknown-type` UI (FR-013), not silence. | ✅ PASS |
| §4 TypeScript standards | `strict`, no `any`; all new types exported from `types/panel.ts` alongside existing ones. | ✅ PASS |
| §7 UI Principles | Built from the MUI theme (no hardcoded colors); responsive via breakpoints; keyboard-operable tray/arrange controls; both themes supported. | ✅ PASS |
| §7 State management | UI state → Zustand (`floatingPanelStore`), correctly; no server state involved, so nothing belongs in TanStack Query. | ✅ PASS |
| §10 Testing | Pure arrangement logic gets exhaustive unit tests; new UI gets component + `jest-axe` a11y tests, in the same change. | ✅ PASS |
| §13 Documentation | `specs/054-*` artifacts + an update to `viewer/README.md`'s panels section. No ADR required — no new datastore, no cross-cutting infrastructure, no public contract change (§17). | ✅ PASS |

**Post-Phase 1 re-check**: ✅ PASS — the design introduces no new layer, no new dependency, and no backend surface. The one judgment call (DOM-query for reserved regions rather than a registration store) is recorded with its trade-off in research D2 and is confined to a single ~20-line adapter module behind a pure interface.

## Project Structure

### Documentation (this feature)

```text
specs/054-panel-placement-reopen/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── arrangement.md          # Pure arrangement engine contract
│   └── reserved-regions.md     # How chrome declares itself unavailable
├── checklists/
│   └── requirements.md  # Spec quality checklist (complete)
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/viewer/panels/
├── layout/                                 # NEW — placement policy
│   ├── arrangement.ts                      # PURE: computeArrangement, findLandingSlot, geometry
│   ├── arrangement.test.ts                 # NEW
│   ├── reservedRegions.ts                  # DOM adapter: collectReservedRects(hostRect)
│   └── reservedRegions.test.ts             # NEW
├── components/
│   ├── PanelDock.tsx                       # NEW — reopen tray rail + "arrange" action
│   ├── PanelDock.test.tsx                  # NEW
│   ├── PanelDock.a11y.test.tsx             # NEW
│   ├── LandingPlaceholder.tsx              # NEW — drag-time ghost outline
│   ├── FloatingPanel.tsx                   # MODIFIED — mark manual placement, emit drag events
│   └── FloatingPanelHost.tsx               # MODIFIED — owns arrange triggers, renders dock/ghost
├── store/
│   ├── floatingPanelStore.ts               # MODIFIED — closedPanels, arrangeAll, applyArrangement
│   └── floatingPanelStore.test.ts          # MODIFIED — new behavior coverage
└── types/panel.ts                          # MODIFIED — manuallyPlaced, ClosedPanelEntry, Rect

src/AskLucy.Web/ClientApp/src/
├── components/workspace-shell/WorkspaceOverlay.tsx   # MODIFIED — declare 3 toolbars reserved
├── viewer/extensions/components/ExtensionToolbar.tsx # MODIFIED — declare reserved, DROP top:460 stopgap
└── features/solar/components/CameraAttitudeWidget.tsx # MODIFIED — declare reserved, DROP top:512 stopgap
```

**Structure Decision**: Frontend-only, following the established `viewer/panels/` feature-domain layout (constitution §4 "frontend organized by feature-domain"). A new `layout/` sibling to the existing `store/`, `chrome/`, `content/`, `actions/` folders holds placement policy — keeping layout math out of both the store (which would make it untestable without Zustand) and the components (§2-VI). The three files outside `viewer/panels/` change by exactly one added attribute each (plus deleting the two stopgaps), so this feature does not spread into the page shell or the solar feature.

## Complexity Tracking

> No constitution violations. Table intentionally empty.
