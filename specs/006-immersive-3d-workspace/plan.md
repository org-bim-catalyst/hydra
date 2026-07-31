# Implementation Plan: Immersive 3D AI Workspace

**Branch**: `006-immersive-3d-workspace` | **Date**: 2026-07-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-immersive-3d-workspace/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Replace the `/chat` route's fixed split layout (permanent sidebar + fixed chat column) with
a full-viewport, continuously rotating 3D sphere background and a floating, collapsible
glassmorphism assistant panel. The sphere is an abstract vertex mesh (not a geographic
globe) that idles via ambient noise-driven displacement and visibly reacts while the
assistant's voice output (TTS) is speaking. Conversation history moves from a permanent
column into a compact selector inside the panel. The approach (research.md): React Three
Fiber + drei over `three`, GPU shader-driven vertex displacement, an approximated
audio-reactive envelope driven by `SpeechSynthesisUtterance` timing events (the browser's
native TTS exposes no analyzable audio stream), three discrete performance/accessibility
quality tiers, and lazy-loading the entire 3D scene behind a `Suspense` boundary so it
never blocks the assistant panel or the initial bundle. No backend, database, or API
changes are required — this is a presentation-layer redesign that reuses the existing
chat/message hooks and endpoints unchanged.

## Technical Context

**Language/Version**: TypeScript ~6.0 (strict), React 19.2, targeting the existing
`AskLucy.Web/ClientApp` Vite 8 SPA.

**Primary Dependencies**: Existing — MUI 9, TanStack Query 5, Zustand 5, react-router 8,
`@tanstack/react-virtual`. New — `three`, `@react-three/fiber`, `@react-three/drei`,
`simplex-noise` (research.md "Summary of new dependencies"), all lazy-loaded behind the
3D scene component and excluded from the initial route bundle.

**Storage**: N/A — no new persistence. Reuses existing conversation/message storage via
the existing `features/chat/api` layer unchanged (data-model.md).

**Testing**: Vitest + Testing Library + `jest-axe` (existing project stack). 3D rendering
logic is kept in plain, WebGL-free functions/hooks (quality-tier selection, envelope math)
so it stays unit-testable without a canvas context; the R3F component tree itself is not
unit-tested.

**Target Platform**: Web (SPA), evergreen desktop and mobile browsers. Must degrade
gracefully without WebGL2 (FR-011) and respect `prefers-reduced-motion` (FR-012).

**Project Type**: Web application — existing ASP.NET Core backend (`src/AskLucy.*`) +
React SPA frontend (`src/AskLucy.Web/ClientApp`). This feature touches only the frontend
project, specifically `features/chat`.

**Performance Goals**: ~60fps on typical modern desktop/laptop hardware; graceful
step-down (reduced detail / paused animation) on lower-end or mobile devices, per FR-020/
SC-010. The assistant panel's responsiveness must never depend on 3D frame rate (FR-017).

**Constraints**: WCAG 2.1 AA (constitution §7) — all functional controls keyboard/
screen-reader operable independent of the 3D scene (FR-013/FR-014); new dependencies must
not regress the `/chat` route's bundle-size budget (constitution §15), satisfied via lazy
loading (research.md §5); first-visit usability of the assistant panel must not wait on
3D asset load (FR-021).

**Scale/Scope**: Single existing route (`/chat`); no change to conversation/message
volume or existing pagination/virtualization limits (data-model.md, reused as-is).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This feature is frontend-only and additive-in-place on one existing route; most backend-
focused articles (§3 Architecture Rules, §5 Database, §6 API Standards, §9 AI Principles)
are **not applicable** — no backend/domain/persistence/API code changes are made
(data-model.md, contracts/README.md).

| Gate | Status | Notes |
|---|---|---|
| §2.I Clean Architecture / Dependency Rule | N/A | No backend layer touched. |
| §2.III Simplicity — DRY/KISS/YAGNI | **PASS** | Conversation selector reuses existing hooks rather than duplicating chat-list logic (research.md §7); 3 discrete quality tiers instead of a bespoke continuous LOD system (research.md §4); TTS reactivity approximated from existing utterance events rather than standing up new backend audio infrastructure (research.md §3). |
| §2.VII Convention Over Configuration | **PASS** | Reuses existing MUI theme, Zustand store pattern (`themeStore` → `assistantPanelStore`), existing chat API/hook modules. |
| §2.VIII No Silent Failures | **PASS, with a task** | 3D scene mount/render failure is caught by a component-scoped error boundary and falls back to the static background (FR-011) rather than failing silently. Note: the *existing* `useTextToSpeech` hook has no `onerror`/user-visible failure path today — pre-existing gap, not introduced by this feature, but since this feature already extends that hook (research.md §3), tasks.md will include wiring a caller-visible error path for it while it's being touched, rather than leaving it newly-relevant-but-still-silent. |
| §7 UI Principles — Design system, MUI/theme | **PASS** | Glassmorphism panel built from MUI `Paper`/`Box`/`Fab`/transitions plus new theme tokens, not a bespoke non-MUI component (research.md §6). Not added to `src/shared` yet (single-feature use, per §7's "two features or foundational primitive" rule). |
| §7 UI Principles — Accessibility (WCAG 2.1 AA) | **PASS** | FR-013/FR-014/FR-015; 3D scene marked decorative/`aria-hidden`; automated `axe` check planned (quickstart.md). |
| §7 UI Principles — Responsive design | **PASS** | FR-010, SC-005; MUI breakpoint system, no fixed-pixel layout. |
| §7 UI Principles — State management | **PASS** | New UI state (`assistantPanelStore`) in Zustand; no server-state duplication — chat data stays in TanStack Query (data-model.md). |
| §7 UI Principles — Performance (bundle budget, lazy loading) | **PASS** | New 3D dependencies are lazy-loaded behind the scene component (research.md §5), never in the initial bundle. |
| §7 UI Principles — Voice output persona | **Deferred, documented via ADR** | The existing browser-default-voice gap is pre-existing and out of scope for this feature (spec Assumptions); research.md §3 flags that a future TTS-provider migration would also resolve it. Recorded formally as [ADR-0005](../../docs/adr/0005-defer-tts-voice-persona-fix.md) per §17, since this feature's T016 modifies the exact file (`useTextToSpeech.ts`) the gap lives in. |
| §10 Testing Standards | **PASS, with documented exception** | Unit/component/a11y tests as before (quickstart.md "Automated coverage expected"). §10 also requires a performance test that fails CI on regression for any path with a stated goal (§15) — FR-020/SC-010's 60fps target. A real GPU frame-rate regression test isn't feasible in this project's existing CI (no GPU runner), and building one would be disproportionate to a layout feature. Mitigation: (a) the `useSceneQualityTier` unit tests (tasks.md T030) cover the degrade-trigger *decision* logic so quality step-down is verified even without a live GPU, and (b) quickstart.md scenario 12 (CPU throttling) is the manual performance check run before merge. Exception recorded here per §16 ("a gate MAY be marked not-applicable with a one-line justification; it MUST NOT be silently skipped"). |
| §16 Quality Gates — Accessibility/Performance review | **Applies at PR time** | Called out explicitly so it isn't skipped for a "just visual" change. |

No violations requiring justification — Complexity Tracking is empty.

**Post-Phase 1 re-check**: Design artifacts (research.md, data-model.md, contracts/README.md,
quickstart.md) confirm no backend/data/API surface was introduced and every gate above
still holds against the concrete file-level plan in Project Structure below — no gate
status changed after design.

## Project Structure

### Documentation (this feature)

```text
specs/006-immersive-3d-workspace/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This feature lives entirely inside the existing frontend project; no backend project
(`AskLucy.Domain`/`Application`/`Infrastructure`/`Persistence`/`Web` API) is touched.

```text
src/AskLucy.Web/ClientApp/src/
├── features/chat/
│   ├── pages/
│   │   └── ChatPage.tsx              # MODIFIED: replaces the split sidebar+column
│   │                                  # layout with <SceneBackground> + <AssistantPanel>
│   ├── components/
│   │   ├── ChatSidebar.tsx           # MODIFIED: data/query/action layer extracted
│   │   │                              # for reuse by ConversationSwitcher (research.md §7)
│   │   ├── AssistantPanel.tsx        # NEW: floating glassmorphism panel shell —
│   │   │                              # hosts chat-specific controls (LanguageSelector,
│   │   │                              # Translate, Generate Image, message list, composer,
│   │   │                              # ConversationSwitcher) only, not brand/account/theme
│   │   ├── AssistantToggleFab.tsx    # NEW: round toggle + unread indicator (FR-016)
│   │   ├── ConversationSwitcher.tsx  # NEW: popover selector reusing chat-list hooks
│   │   └── MinimalTopBar.tsx         # NEW: thin persistent bar OUTSIDE the panel
│   │                                  # (BrandMark, theme toggle, UserMenu) — FR-015
│   ├── scene/                        # NEW: all 3D-specific code, isolated so it can be
│   │   │                              # lazy-loaded as one chunk (research.md §5)
│   │   ├── SceneBackground.tsx       # NEW: React.lazy/Suspense boundary + placeholder
│   │   │                              # cross-fade
│   │   ├── ReactiveSphere.tsx        # NEW: R3F component — geometry + shader material
│   │   ├── useSceneQualityTier.ts    # NEW: WebGL/perf/reduced-motion → quality tier
│   │   │                              # (WebGL-free, unit-testable)
│   │   └── sphere.vert.glsl / .frag.glsl  # NEW: noise-driven vertex displacement shader
│   ├── voice/
│   │   └── useTextToSpeech.ts        # MODIFIED: adds isSpeaking/intensity envelope
│   │                                  # from utterance boundary events (research.md §3)
│   └── hooks/
│       └── useChats.ts, useConversationActions.ts   # UNCHANGED — reused as-is
├── store/
│   └── assistantPanelStore.ts        # NEW: Zustand store, follows themeStore.ts pattern
└── theme/tokens/
    └── glass.ts                      # NEW: light/dark glassmorphism tokens
                                       # (backdrop blur + translucent surface colors)
```

**Structure Decision**: Single existing web application (ASP.NET Core backend +
React/Vite frontend under `src/AskLucy.Web/ClientApp`), per the project's established
layout. This feature adds a new `features/chat/scene/` submodule (isolated so its 3D
dependencies form one lazy-loaded chunk) plus new/modified components inside the existing
`features/chat` feature folder, one new Zustand store, and one new theme-tokens file — no
new top-level project, no backend changes.

## Complexity Tracking

*No entries — Constitution Check recorded no violations requiring justification.*
