# Implementation Plan: Flumeria Studio Workspace Shell

**Branch**: `024-flumeria-studio-shell` | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/024-flumeria-studio-shell/spec.md`

## Summary

Rename the authenticated workspace from "Chat" (`/chat`) to "Flumeria Studio" (`/studio`), and rebuild it around a full-viewport neutral placeholder surface with no permanent toolbar/sidebar/menu. All controls — 2D/3D view mode, layers, navigation, selection, analysis (the last four shown as "coming soon" placeholders), and the AI chat assistant — are reached through a new reusable circular-control system (`CircularAction` → `ExpandableActionGroup`/`FloatingPanel`, composed by `FloatingToolbar`/`ContextualToolbar`, coordinated by `WorkspaceOverlay`) that expands in place and collapses, with at most one control expanded at a time. The existing AI particle-sphere visualization relocates out of the background into its own small floating "AI Presence Card." The technical approach reuses the existing MUI theme/token system (`radius.pill`, `zIndex`, `createMotionTokens`'s reduced-motion-aware `theme.transitions`) and the existing glass-panel pattern (`createGlassTokens`) rather than introducing a new animation library or design language; the six new components live alongside existing cross-feature primitives in `src/components/`, and a new session-scoped Zustand store (`workspaceOverlayStore`) replaces `assistantPanelStore` as the single source of truth for which one control is expanded.

## Technical Context

**Language/Version**: TypeScript ~6.0.2, React 19.2.7 (frontend-only feature; no backend/API changes)

**Primary Dependencies**: MUI 9 (`@mui/material`, `@mui/icons-material`) for all new component primitives; `react-router` 8 for the `/studio` route and `/chat` → `/studio` redirect; `zustand` 5 for the new overlay/view-mode state; existing `@react-three/fiber`/`three`/`@react-three/postprocessing` stack reused unmodified for the relocated AI Presence Card (`SceneBackground`/`ReactiveSphere`/`ParticleSphereBloom`, `useVoiceOutput`). No new dependency is added — expand/collapse motion uses MUI's own transition primitives (`Fade`/`Grow`/`Collapse`) driven by the existing `theme.transitions` (already reduced-motion-aware via `createMotionTokens`), not a new animation library, per constitution §2.III (YAGNI) and §7 (compose from the existing MUI theme before a bespoke component).

**Storage**: N/A — no new persisted data. All new UI state (which control is expanded, current view mode) is client-side, session-scoped (in-memory Zustand, no `persist` middleware), per the spec's Assumptions.

**Testing**: Vitest + React Testing Library (interaction/keyboard tests) + `jest-axe` (accessibility), matching the existing `*.test.tsx`/`*.a11y.test.tsx` convention already used throughout `src/features/chat` and `src/components`.

**Target Platform**: Existing browser-based SPA (`src/AskLucy.Web/ClientApp`), served by the ASP.NET Core host; desktop, tablet, and mobile viewport widths (per FR-020/US5).

**Project Type**: Web application — this feature is frontend-only within the existing backend (ASP.NET Core) + frontend (`ClientApp`) solution structure; no backend project is touched.

**Performance Goals**: Expand/collapse transitions complete in ~300ms and read as smooth (SC-003), consistent with the existing `motion.ts` `standard` duration (200ms) plus MUI's own transition curves — no new performance budget beyond what the current bundle/route already targets. The relocated AI Presence Card reuses the existing particle-sphere engine's established 60fps-desktop / graceful-degradation target (spec 011) unchanged, since it is a smaller card now rather than a full-viewport scene.

**Constraints**: No permanent toolbar/sidebar/menu/settings panel consuming fixed screen space (FR-004); full keyboard + mouse/touch operability (FR-009); WCAG 2.1 AA (constitution §7), including exposing expand/collapse state to assistive technology (FR-019) and honoring `prefers-reduced-motion` (FR-018 — already handled globally by `createMotionTokens`, so no new reduced-motion branching should be written per component); MUI-only component composition, no new UI kit or animation library (constitution §7, §2.III); at most one expanded control at a time (FR-015); existing chat functionality (send, stream, switch conversation) must regress zero behavior once relocated behind the chat control (FR-014/SC-006).

**Scale/Scope**: One renamed route (`/chat` → `/studio`) plus a compatibility redirect; six new reusable components (`CircularAction`, `ExpandableActionGroup`, `FloatingToolbar`, `ContextualToolbar`, `FloatingPanel`, `WorkspaceOverlay`) under `src/components/workspace-shell/`; one new Zustand store (`workspaceOverlayStore`) replacing `assistantPanelStore`; rework of `ChatPage.tsx`, `AssistantPanel.tsx`, and `AssistantToggleFab.tsx` to sit inside the new overlay; a new `WorkspaceSurface` placeholder background component; a new `AiPresenceCard` component hosting the relocated particle-sphere scene; an `account` `ControlDefinition` reusing the existing `UserMenu` component (unchanged) plus the theme toggle, reached via the same circular-control pattern as every other entry point — preserving today's `MinimalTopBar`-only access to Profile/Settings/cross-app navigation/Log out (FR-024, added post-`/speckit-analyze`). Ten existing files contain a hardcoded `/chat` route literal (`router.tsx`, `AppShell.tsx`, `ErrorPage.tsx`, `AdminRoute.tsx`, `PublicOnlyRoute.tsx` (+ its test), `LoginPage.tsx`, `ExternalLoginCompletePage.tsx`, `LandingCtaBar.tsx` (+ its test)) and need updating to `/studio`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature is frontend-only (no Domain/Application/Infrastructure/Api changes, no database schema, no new REST endpoints), so the backend-specific gates in §3 (Clean Architecture layering), §5 (Database Principles), §6 (API Standards), and §9 (AI provider/model abstraction) are **N/A** — nothing in this feature touches those layers.

| Gate | Status | Notes |
|---|---|---|
| §2.I Clean Architecture / Dependency Rule | N/A | No backend layers touched. |
| §2.III Simplicity — DRY/KISS/YAGNI | PASS | No new dependency for animation; reuses existing `theme.transitions`/glass tokens instead of a bespoke motion system. Placeholder tool categories are static config, not speculative infrastructure. |
| §2.VIII No Silent Failures | PASS | This feature adds no new async operations of its own (expand/collapse is synchronous local state). Existing chat error paths (message send failure, TTS failure, voice-preference failure — all surfaced via `Snackbar`/`Alert` in `ChatPage.tsx`) MUST be carried over unchanged when `ChatPage` is restructured, not dropped. |
| §7 UI Principles — Design system | PASS | All six new components are composed from existing MUI primitives (`Fab`, `Paper`, `Popper`/`ClickAwayListener`, `Collapse`/`Grow`) and existing theme tokens (`radius.pill`, `zIndex`, `createGlassTokens`), not a new bespoke visual system. Each is used by ≥2 call sites (viewer tools and chat), satisfying the "shared component" bar. |
| §7 UI Principles — Accessibility | PASS (design obligation, verified in Phase 1/tasks) | FR-009/FR-019 require full keyboard operability and AT-visible expand/collapse state; `*.a11y.test.tsx` coverage is required for every new component, matching existing convention. |
| §7 UI Principles — Responsive design | PASS | FR-020/US5 require MUI breakpoint-based layout, no fixed-pixel layout, matching `MinimalTopBar`'s existing responsive pattern. |
| §7 UI Principles — Theming | PASS | New components must read colors from the MUI theme (`glass.background`, `theme.palette.*`), never hardcode colors — same rule `MinimalTopBar`/`AssistantPanel` already follow. |
| §7 UI Principles — State management | PASS | New transient UI state (expanded control id, view mode) lives in a new Zustand store, not duplicated into TanStack Query; no server state is introduced by this feature. |
| §7 Voice output persona | N/A (unaffected) | The relocated AI Presence Card reuses `useVoiceOutput`/TTS unchanged; this feature does not touch voice persona selection. |
| §10 Testing Standards | PASS (obligation) | Vitest/RTL + `jest-axe` tests required for all six new components and for the reworked `ChatPage`/`AssistantPanel` integration, matching existing coverage pattern. |
| §15 Performance — Frontend | PASS | New components are added to the already-lazy-loaded `/studio` route; the `AiPresenceCard`'s three.js scene continues to be lazy-loaded exactly as `SceneBackground` is today. |

**Result**: No violations requiring justification. Complexity Tracking table below is empty.

### Post-Design Re-Check (after Phase 1)

Re-evaluated against `research.md`, `data-model.md`, and `contracts/workspace-shell-components.md`: still no new dependency introduced, still zero backend/database/API surface, `workspaceOverlayStore` is a single-responsibility store (§2.II SRP) rather than a god-store, and the component contracts keep each of the six primitives narrow and composable (§2.II ISP — `CircularAction` doesn't know about panels, `FloatingPanel`/`ExpandableActionGroup` don't know about the store). No gate regressed; no new entries needed in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/024-flumeria-studio-shell/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   └── workspace-shell-components.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is an existing ASP.NET Core (backend) + React/Vite (frontend) web application. This feature only touches the frontend, under `src/AskLucy.Web/ClientApp/`.

```text
src/AskLucy.Web/ClientApp/src/
├── components/                          # existing cross-feature shared primitives (AppShell, EmptyState, ErrorState, ...)
│   └── workspace-shell/                 # NEW — the six reusable components this feature establishes
│       ├── CircularAction.tsx
│       ├── ExpandableActionGroup.tsx
│       ├── FloatingToolbar.tsx
│       ├── ContextualToolbar.tsx
│       ├── FloatingPanel.tsx
│       ├── WorkspaceOverlay.tsx
│       └── *.a11y.test.tsx / *.test.tsx  # one pair per component, matching existing convention
│
├── store/
│   ├── workspaceOverlayStore.ts         # NEW — replaces assistantPanelStore.ts (which is removed)
│   └── workspaceOverlayStore.test.ts    # NEW
│
├── features/
│   └── chat/
│       ├── pages/ChatPage.tsx           # MODIFIED — mounts WorkspaceOverlay + WorkspaceSurface + AiPresenceCard instead of SceneBackground/AssistantToggleFab directly
│       ├── components/
│       │   ├── AssistantPanel.tsx       # MODIFIED — becomes the content of a FloatingPanel opened by the chat CircularAction
│       │   ├── AssistantToggleFab.tsx   # REMOVED — superseded by the chat CircularAction
│       │   ├── WorkspaceSurface.tsx     # NEW — full-viewport neutral placeholder (soft alternating gradient)
│       │   └── AiPresenceCard.tsx       # NEW — small floating rounded-square card hosting the relocated particle sphere
│       └── scene/                       # UNCHANGED — SceneBackground/ReactiveSphere/ParticleSphereBloom reused as-is inside AiPresenceCard
│
├── routes/
│   └── router.tsx                       # MODIFIED — /studio replaces /chat; /chat becomes a redirect to /studio
│
└── components/AppShell.tsx, ErrorPage.tsx; routes/AdminRoute.tsx, PublicOnlyRoute.tsx;
    features/auth/pages/LoginPage.tsx, ExternalLoginCompletePage.tsx;
    features/landing/components/LandingCtaBar.tsx                     # MODIFIED — hardcoded '/chat' literals updated to '/studio'
```

**Structure Decision**: Web application, existing structure preserved. The six reusable shell components are added under `src/components/workspace-shell/` — a subfolder of the existing `src/components/` cross-feature primitives directory — rather than creating a new top-level `src/shared/` directory. The constitution (§4 "Folder structure") names `src/shared` as the convention for cross-feature primitives, but this codebase's actual established convention (verified against `AppShell.tsx`, `EmptyState.tsx`, `ErrorState.tsx`, `BrandMark.tsx`, etc.) already uses top-level `src/components/` for exactly this purpose; per constitution §2.VII (Convention Over Configuration), the existing project convention is followed rather than introducing a second, competing "shared primitives" location. This is recorded as a decision in `research.md`.

## Complexity Tracking

*No entries — Constitution Check found no violations requiring justification.*
