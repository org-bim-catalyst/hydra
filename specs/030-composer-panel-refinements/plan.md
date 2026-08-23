# Implementation Plan: Composer & Panel Layout Refinements

**Branch**: `030-composer-panel-refinements` | **Date**: 2026-08-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/030-composer-panel-refinements/spec.md`

## Summary

Restructure `ChatComposer.tsx` from a single-row pill into a two-row rounded-rectangle
(text area on top, a fixed footer row of every control button on the bottom), cap the
`TextField`'s growth so it scrolls internally past 6 lines instead of growing the whole
composer unbounded, add a persisted full-height/half-height toggle to `ExpandedChatPanel.tsx`
placed next to the existing new-chat button, and add tooltips to every icon-only button in
both components that is still missing one. Purely a frontend layout/accessibility change to
two already-consolidated components from specs/029-fix-chat-widget-bugs — no backend, API,
or database changes.

## Technical Context

**Language/Version**: TypeScript 5.x, React 19

**Primary Dependencies**: Material UI (MUI) v6/v7 (`Paper`, `Stack`, `Box`, `TextField`,
`IconButton`, `Tooltip`), Zustand 5 (+ `zustand/middleware` `persist`), Vite, `@remixicon/react`

**Storage**: N/A for backend/SQL Server — the one piece of new state (the panel's chosen
height) is persisted client-side only, via a Zustand `persist` store backed by
`localStorage` (same pattern as `src/.../store/themeStore.ts`), per spec.md's Clarifications
answer and Assumptions. No new API endpoint, DTO, or database table.

**Testing**: Vitest + React Testing Library (component tests), existing `*.a11y.test.tsx`
pattern (jest-axe or equivalent) for accessibility assertions, matching
`ExpandedChatPanel.a11y.test.tsx`/`ChatComposer.test.tsx`'s established conventions.

**Target Platform**: Web (SPA), `src/AskLucy.Web/ClientApp` — desktop and mobile browser
viewports via MUI responsive breakpoints.

**Project Type**: Web application — this feature is frontend-only within the existing
`AskLucy.Web/ClientApp` React SPA; no backend/`AskLucy.Api`/`AskLucy.Infrastructure` changes.

**Performance Goals**: Textarea growth/scroll-cap and the panel height toggle must animate
without visible jank on typical hardware (no layout thrash from the growing/capped
`TextField`); consistent with existing MUI transition patterns already used elsewhere in
these two components (e.g. the composer's existing `focus-within` transition).

**Constraints**: Must preserve 100% of existing composer/voice/panel behavior from
specs/029-fix-chat-widget-bugs (FR-014) — this is layout, sizing, placement, and
accessibility-labeling only. Must meet WCAG 2.1 AA (constitution §7). Must not introduce a
new shared component without reuse justification (constitution §7 design-system rule) —
prefer extending the two existing components over new bespoke ones.

**Scale/Scope**: Two components modified (`ChatComposer.tsx`, `ExpandedChatPanel.tsx`), one
new small Zustand store, associated test updates — no new pages, routes, or backend surface.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This is a frontend-only UI/UX refinement with no new backend surface, so most
architecture-layer gates (§1–§6, §8, §9) are **N/A** — no Domain/Application/Infrastructure
code changes, no new API endpoint, no AI/provider change, no new persisted server-side data.

Applicable gates:

| Gate | Status | Notes |
|------|--------|-------|
| §7 State management (Zustand for client/UI state, not duplicated into TanStack Query) | PASS | New panel-height preference is genuinely client-only UI state (no server round trip) — a `persist`-backed Zustand store is the correct, already-established pattern (`themeStore.ts`), not a TanStack Query concern. |
| §7 Design system / component reuse | PASS | No new shared component introduced; both target components already exist and are extended in place. |
| §7 Accessibility (WCAG 2.1 AA) | PASS (verify in Phase 1/tasks) | Every icon-only button gets an MUI `Tooltip` + `aria-label`, following the pattern already used for existing controls in `ChatComposer.tsx` (e.g. the mute/translate buttons). |
| §7 Responsive design | PASS (verify in Phase 1/tasks) | Rounded-rectangle/footer-row layout and the full-height panel state must both work across the existing `xs`/`sm` breakpoints already used in these two files. |
| §10 Testing (component + a11y tests for changed behavior) | PASS (planned in tasks) | New/updated Vitest+RTL tests for both components plus the new store; a11y assertions extended for the new controls. |
| §16 Accessibility review for user-facing UI changes | PASS (planned) | Satisfied by the a11y test coverage above plus manual tooltip/keyboard-focus verification during implementation, mirroring specs/029's approach. |

No violations identified — Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/030-composer-panel-refinements/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/
├── features/chat/
│   ├── components/
│   │   ├── ChatComposer.tsx              # MODIFIED: two-row layout, capped/scrolling
│   │   │                                    textarea, tooltips on remaining un-labeled
│   │   │                                    icon buttons
│   │   ├── ChatComposer.test.tsx         # MODIFIED: new layout/scroll/tooltip assertions
│   │   ├── ExpandedChatPanel.tsx         # MODIFIED: resize/toggle button next to the
│   │   │                                    "+" new-chat button, full-height sizing,
│   │   │                                    tooltips on header icon buttons
│   │   ├── ExpandedChatPanel.test.tsx    # MODIFIED
│   │   └── ExpandedChatPanel.a11y.test.tsx # MODIFIED
│   ├── chatPanelSizeStore.ts             # NEW: small persisted Zustand store for the
│   │                                        half-height/full-height preference
│   │                                        (mirrors src/store/themeStore.ts)
│   └── chatPanelSizeStore.test.ts        # NEW
└── theme.ts (or theme/*)                 # referenced only (radius tokens already used
                                             by both components) — not expected to change
```

**Structure Decision**: Extend the two existing components in place under
`src/AskLucy.Web/ClientApp/src/features/chat/components/` (already the sole owners of this
UI per specs/029-fix-chat-widget-bugs); add one new flat store file directly under
`src/AskLucy.Web/ClientApp/src/features/chat/`, matching the existing flat placement of
`activeConversationStore.ts` in the same folder (not a nested `store/` subfolder, which this
feature area doesn't use). No backend project is touched.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
