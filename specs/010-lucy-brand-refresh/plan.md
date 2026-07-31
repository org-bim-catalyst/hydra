# Implementation Plan: Lucy Brand & Voice Refresh

**Branch**: `010-lucy-brand-refresh` | **Date**: 2026-07-31 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-lucy-brand-refresh/spec.md`

## Summary

Frontend-only refresh of four brand-facing surfaces in the AI workspace: (1) close the
long-deferred TTS voice-persona gap (ADR-0005) by adding a curated, versioned
language/browser voice mapping with a scored heuristic fallback in `useTextToSpeech.ts`;
(2) replace the workspace's solid-shaded `ReactiveSphere` icosahedron with a `THREE.Points`
dot-mesh sphere whose idle/reactive shader colors are driven by MUI theme tokens instead of
fixed hex literals; (3) introduce a canonical Lucy portrait asset on the `AssistantToggleFab`,
`AuthLayout` (login/register/other pre-auth pages); (4) restyle those same auth pages within
the existing "drafting table" design language. No backend, API, or database changes.

## Technical Context

**Language/Version**: TypeScript 6.0 (frontend-only change; no backend/.NET changes)

**Primary Dependencies**: React 19, MUI 9 (theme provider, `sx`), `@react-three/fiber` 9 /
`@react-three/drei` 10 / `three` 0.185 (already used by `ReactiveSphere`/`SceneBackground` —
reused, not newly added), `simplex-noise` 4 (already used for idle wobble), browser-native
Web Speech API (`SpeechSynthesis`/`SpeechSynthesisVoice`) — no new TTS provider/SDK.

**Storage**: N/A — the curated voice mapping ships as a static, versioned TypeScript config
module in the frontend bundle, not a database table or API-fetched resource (spec Assumption:
client-side voice selection/tuning only, no server-rendered pipeline).

**Testing**: Vitest + Testing Library + `jest-axe` (existing stack); manual cross-browser
audit for the voice persona (Vitest cannot execute real `SpeechSynthesis` voice catalogs).

**Target Platform**: Web browsers — Chromium, Firefox, and WebKit/Safari engines, desktop and
mobile equivalents (incl. iOS Safari), per spec Clarifications.

**Project Type**: Web application — frontend-only feature inside the existing
`src/AskLucy.Web/ClientApp` React SPA. No backend (`src/AskLucy.Web` API/Application/
Infrastructure/Domain) changes; no new endpoints, no new persistence.

**Performance Goals**: Dot-mesh sphere holds the existing 'full'-tier 60fps target used by
today's `ReactiveSphere` (enforced by the existing `PerformanceMonitor` one-way ratchet to
'reduced' in `useSceneQualityTier`) — this feature must not regress that budget. Theme-driven
dot color changes must apply with no visually perceptible delay (SC-004).

**Constraints**: No new external runtime dependency for rendering (reuse three.js/R3F already
in the bundle); WCAG 2.1 AA (constitution §7); dot mesh must not change the sphere's screen
position/size/role in the workspace layout (spec Assumption); voice selection is 100%
client-side (no new API surface); dot colors and portrait styling must derive from
`theme/tokens/palette.ts`, never hardcode a color that bypasses the theme (constitution §7
Theming).

**Scale/Scope**: 4 prioritized user stories, 17 functional requirements. Voice mapping covers
5 languages (en, ar, es, fr, de) × 3 browser engines = up to 15 curated (language, platform)
entries, each with a documented heuristic fallback. Touches three frontend areas —
`features/chat/scene/*`, `features/chat/voice/*`, `components/AuthLayout.tsx` + auth pages —
plus one new shared branding asset location.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Applies? | Assessment |
|---|---|---|
| §7 UI — Voice output persona | Yes | This feature's entire P1 story exists to satisfy this rule (currently violated per ADR-0005). Plan closes it via curated mapping + heuristic fallback (FR-001–005). **Pass** (design target, not yet implemented). |
| §7 UI — Theming (no hardcoded colors bypassing theme) | Yes | Current `ReactiveSphere` hardcodes `uColorIdle`/`uColorReactive` as literal hex, identical in both modes — itself a pre-existing theming gap. Plan derives dot-mesh colors from `theme/tokens/palette.ts` per mode (research.md §2). **Pass** (design target). |
| §7 UI — Accessibility (WCAG 2.1 AA) | Yes | Dot mesh stays `aria-hidden` (decorative, like today's sphere/`StaticFallback`); Lucy portrait requires alt text everywhere (FR-013); redesigned auth pages keep focus states/contrast. Verified via existing `jest-axe` a11y test pattern (`*.a11y.test.tsx`). **Pass**. |
| §7 UI — Responsive design | Yes | `AuthLayout` already stacks column-on-mobile; redesign refines spacing/typography within that structure rather than replacing it (FR-016). **Pass**. |
| §7 UI — Design system (existing MUI theme/components first) | Yes | Reuses `AuthLayout`, `BrandMark`, `AppFooter`, existing MUI components; Lucy portrait is a new image asset, not a new component library. **Pass**. |
| §7 UI — Internationalization | Marginal | No i18n framework is introduced by this feature; the 5-language `LANGUAGES` list in `LanguageSelector.tsx` is reused unchanged. UI copy stays English-only (pre-existing state, not a regression). **N/A — no change to i18n posture**. |
| §2.VIII No Silent Failures | Yes | TTS failure path (FR-005) and Lucy-portrait-load failure path (FR-014) both require a visible/graceful outcome, extending the existing pattern already in `useTextToSpeech.ts`'s `onerror` and `SceneBackground`'s error boundary. **Pass**. |
| §2.III Simplicity/YAGNI | Yes | Voice mapping ships as a plain versioned config module (no admin UI, no new persistence layer, no new provider abstraction) — the smallest design that satisfies FR-003/FR-004. **Pass**. |
| §3 Architecture (Clean Architecture layers) | No | Feature makes zero backend changes; nothing crosses the `Domain`/`Application`/`Infrastructure`/`Api` boundary. **N/A**. |
| §10 Testing Standards | Yes | Unit tests for the voice-selection/heuristic logic and updated component/a11y tests for the changed UI are required in the same PR (existing `useTextToSpeech.test.ts`, `AssistantToggleFab.test.tsx`, `ChatPage.a11y.test.tsx` patterns extend directly). **Pass** (planned in tasks phase). |
| §13 Documentation (ADRs) | Yes | This feature closes the gap ADR-0005 explicitly deferred. Tasks phase adds a short follow-up note/status update to `docs/adr/0005-defer-tts-voice-persona-fix.md` recording resolution — no new ADR needed (no new architectural pattern is introduced). **Pass** (planned). |

No violations requiring justification — **Complexity Tracking is empty**.

**Post-Phase-1 re-check**: research.md and data-model.md introduce no new external
dependencies, no backend/API surface, and no persisted state. `selectPersonaVoice` is
specified as a pure function precisely to satisfy §10's "unit-testable without DOM/network"
requirement; `dotMeshTheme.ts` derives colors from existing `palette.ts` tokens only (no new
colors), satisfying §7 Theming. Table above still holds — **no new violations, gate remains
Pass**.

## Project Structure

### Documentation (this feature)

```text
specs/010-lucy-brand-refresh/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   └── voice-persona-mapping.md
└── tasks.md               # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/
├── assets/
│   └── branding/
│       └── lucy-portrait.<ext>        # NEW — canonical Lucy character asset (+ variants as needed)
├── components/
│   ├── AuthLayout.tsx                 # MODIFIED — redesign (FR-015/016) + Lucy portrait (FR-011)
│   └── BrandMark.tsx                  # unchanged, still used alongside the portrait
├── theme/
│   └── tokens/
│       └── palette.ts                 # unchanged — read from, not modified, by the dot-mesh color mapping
├── features/
│   ├── auth/pages/
│   │   ├── LoginPage.tsx              # MODIFIED — portrait via AuthLayout (FR-011)
│   │   ├── RegisterPage.tsx           # MODIFIED — portrait via AuthLayout (FR-012)
│   │   ├── ConfirmEmailPage.tsx       # MODIFIED — portrait via AuthLayout (FR-012)
│   │   ├── ConfirmEmailChangePage.tsx # MODIFIED — portrait via AuthLayout (FR-012)
│   │   └── ExternalLoginCompletePage.tsx # MODIFIED — portrait via AuthLayout (FR-012)
│   └── chat/
│       ├── branding/
│       │   └── LucyPortrait.tsx       # NEW — shared portrait component (variant/alt/onError, FR-013/014)
│       ├── components/
│       │   └── AssistantToggleFab.tsx # MODIFIED — Lucy portrait on the toggle (FR-010)
│       ├── scene/
│       │   ├── ReactiveSphere.tsx     # MODIFIED — icosahedron mesh → THREE.Points dot mesh (FR-006/007); reads useThemeStore directly for dot colors (research.md §2)
│       │   ├── sphere.vert.glsl       # MODIFIED — per-point displacement instead of surface-vertex
│       │   ├── sphere.frag.glsl       # MODIFIED — round point-sprite shading, theme-driven colors
│       │   ├── dotMeshTheme.ts        # NEW — maps theme mode → idle/reactive dot colors (FR-008)
│       │   └── SceneBackground.tsx    # unchanged — StaticFallback was already theme-aware; theme mode is read directly inside ReactiveSphere, not passed down (research.md §2)
│       └── voice/
│           ├── useTextToSpeech.ts     # MODIFIED — persona-matching voice selection (FR-001–005)
│           ├── voicePersonaMap.ts     # NEW — curated (language, browser engine) → voice-name mapping (FR-003)
│           └── selectPersonaVoice.ts  # NEW — curated lookup + scored heuristic fallback (FR-004)
└── ... (all other existing paths unchanged)
```

**Structure Decision**: Pure extension of the existing `src/features/<domain>` frontend
convention (constitution §4 Folder structure) — no new top-level project, no backend project
touched. A new `src/assets/branding/` directory is introduced as the first cross-feature
static-asset location (there was none previously), following the same "cross-feature
primitives under `src/shared`"-style rule already established for non-feature-scoped code.

## Complexity Tracking

*No entries — Constitution Check reported no violations requiring justification.*
