# Implementation Plan: Composer Interaction States Redesign

**Branch**: `039-composer-interaction-states-redesign` | **Date**: 2026-08-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/039-composer-interaction-states-redesign/spec.md`

## Summary

Redesign the Expanded chat panel's composer (`ChatComposer.tsx`) so its action buttons
reflect only the current interaction state — empty, typing, click-to-talk recording,
hold-to-talk recording, or continuous-conversation (idle-listening / typing) — per the
mockups in `docs/UI-UX-Functional-Requirements.md` / `docs/images/`. Most of the underlying
voice-capture mechanics (tap-vs-hold gesture disambiguation, transcription, the persisted
`PushToTalk`/`Continuous` mode preference) already exist and are reused as-is; the work is
primarily: (1) making the composer's visible action set state-dependent instead of
always-showing every control, (2) collapsing today's two-step "switch mode, then separately
start listening" into the resolved one-click hybrid for continuous conversation (see
Clarifications), (3) removing the saved-prompts button everywhere, (4) swapping the
composer height-control icons, and (5) adding a new per-reply replay/stop control to
`MessageBubble.tsx`, backed by the existing shared `useVoiceOutput` TTS hook's unchanged
`speak`/`stop`/`isSpeaking` API — `ChatPage.tsx` tracks which message id is currently
targeted, so the hook itself needs no signature change (see contracts/reply-playback-control.md).

## Technical Context

**Language/Version**: TypeScript 5.x (strict), React 19.2

**Primary Dependencies**: MUI 9 (`@mui/material`), `@remixicon/react` 4.9 (icon set —
`RiVoiceprintLine`, `RiExpandDiagonalLine`, `RiCollapseDiagonalLine`, `RiMicFill`,
`RiStopLine`, `RiStopFill`, `RiPlayFill` all confirmed present), Zustand 5
(`voicePreferencesStore`), TanStack Query (`useVoicePreferencesQuery`), Vite 8

**Storage**: N/A — reuses the existing persisted voice-mode preference
(`voicePreferencesStore.ts` / backend `voiceApi.ts`); no new persisted state is introduced
(per Clarifications)

**Testing**: Vitest 4 + `@testing-library/react` 16, plus `vitest-axe`-driven a11y test
files (`*.a11y.test.tsx`) alongside each changed component, matching existing convention
(`ChatComposer.test.tsx`, `ChatPage.a11y.test.tsx`)

**Target Platform**: Browser (desktop + touch), served from the ASP.NET Core-hosted React SPA

**Project Type**: Web application — this feature is frontend-only
(`src/AskLucy.Web/ClientApp`); no backend/API contract changes

**Performance Goals**: No new performance budget beyond the project's existing route-level
and interaction responsiveness expectations (constitution §15) — this is a UI-state/visual
change over already-working audio pipelines, not a new hot path

**Constraints**: Must preserve all currently-working gesture/transcription/mute/TTS
mechanics (`useVoiceRecorder`, `useSpeechRecognition`, `useTextToSpeech`, `useVoiceOutput`)
completely unmodified — including `useVoiceOutput.ts` itself, which needs no signature
change (see contracts/reply-playback-control.md); changes are limited to which controls
render, when, plus one new piece of page-level state in `ChatPage.tsx` tracking which
message id `useVoiceOutput` is currently targeting

**Scale/Scope**: Single feature area (one composer, one message-bubble component, one
settings-adjacent preference control); no data volume/scale dimension

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Applies? | Assessment |
|---|---|---|
| §2.I Clean Architecture / Dependency Rule | Yes (trivially) | Frontend-only change; no Domain/Application/Infrastructure code touched. `ChatComposer`/`MessageBubble` remain presentation components consuming hooks/stores already defined at the correct layer (client-side only — this whole feature has no backend layer). **Pass.** |
| §2.VI Separation of Concerns | Yes | Business logic (gesture disambiguation, mute/mode state, TTS playback) already lives in hooks/stores (`useVoiceRecorder`, `useVoicePreferencesStore`, `useVoiceOutput`) outside the presentational components; this feature adds *no* new business logic to components, only conditional rendering and one new piece of page-level state (which message id is currently targeted for playback). **Pass.** |
| §2.VIII No Silent Failures | Yes | FR-026 explicitly requires every recording/transcription/playback failure to surface visibly; existing `captureError`/`Snackbar` and `useVoiceOutput`'s `error` state already provide this pattern to extend, not invent. **Pass, must verify in review.** |
| §7 UI Principles — Design system | Yes | All new controls are `IconButton`/`Tooltip` from the existing MUI theme + `@remixicon/react`, matching every sibling control already in `ChatComposer.tsx`/`ExpandedChatPanel.tsx`. No bespoke component introduced. **Pass.** |
| §7 UI Principles — Accessibility (WCAG 2.1 AA) | Yes | Existing components already carry `aria-label`/`Tooltip` pairs and keyboard handling (`onKeyDown`/`onKeyUp` mirroring pointer gestures for the mic). New controls (replay/stop, voiceprint) must follow the same pattern; a11y test files are planned per component. **Pass, enforced via §10 a11y tests.** |
| §7 UI Principles — State management | Yes | Server/persisted state (`conversationMode`, `isMuted`) stays in `voicePreferencesStore`/TanStack Query; new transient UI state (which reply is currently playing) is local component/page state, not duplicated into Zustand. **Pass.** |
| §7 UI Principles — Voice output persona | Yes | Replay reuses `useVoiceOutput`, which already enforces the consistent persona voice (`useTextToSpeech` → `selectPersonaVoice`); replay does not introduce a second speech path. **Pass.** |
| §10 Testing Standards | Yes | Unit/component tests required for changed behavior (composer state transitions, replay control); a11y tests required for new interactive UI. **Pass, tracked into tasks.** |
| §18 AI Coding Agent Rules — no invented requirements | Yes | The one genuine architectural fork (continuous-conversation entry point) was resolved via `/speckit-clarify` rather than guessed; documented in spec.md Clarifications. **Pass.** |

No violations requiring justification. **Complexity Tracking is empty.**

**Post-Phase-1 re-check**: `research.md` and `data-model.md`/`contracts/` confirm no new
persisted state, no new backend surface, no new shared component library additions, and no
parallel audio/gesture pipeline (Decisions 1–8) — every gate above still holds after design.
No new violations introduced by the Phase 1 artifacts.

## Project Structure

### Documentation (this feature)

```text
specs/039-composer-interaction-states-redesign/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── composer-voice-states.md
│   └── reply-playback-control.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

This is a web application; the backend (`src/AskLucy.Api`, `src/AskLucy.Application`,
`src/AskLucy.Domain`, `src/AskLucy.Infrastructure*`) is **not touched** by this feature — no
new/changed endpoints, no schema changes. All work is inside the existing frontend project:

```text
src/AskLucy.Web/ClientApp/src/features/chat/
├── components/
│   ├── ChatComposer.tsx              # MODIFIED — state-dependent action visibility (US1–4, US6),
│   │                                  # one-click continuous-conversation entry (Clarifications)
│   ├── ChatComposer.test.tsx         # MODIFIED
│   ├── RecordingReviewControls.tsx   # UNCHANGED — reused as-is (confirm/cancel icons already
│   │                                  # match Figure 3: RiCheckLine/RiCloseLine)
│   ├── CollapsedVoiceControls.tsx    # MODIFIED (icon-only) — fingerprint-line → voiceprint-line
│   │                                  # kept consistent with ChatComposer; layout unchanged
│   │                                  # (no mockup covers the Collapsed widget — see research.md)
│   ├── ExpandedChatPanel.tsx         # MODIFIED — height-control icon swap (US6/FR-019)
│   ├── ExpandedChatPanel.test.tsx    # MODIFIED (if exists) / new assertions
│   ├── MessageBubble.tsx             # MODIFIED — new replay/stop control (US5)
│   └── MessageBubble.test.tsx        # NEW or MODIFIED
├── voice/
│   └── useVoiceOutput.ts             # UNCHANGED — `speak`/`stop`/`isSpeaking` signature is
│                                      # reused as-is; ChatPage.tsx tracks the target message id
└── pages/
    ├── ChatPage.tsx                  # MODIFIED — owns "which message id is currently playing"
    │                                  # state; wires replay/stop callbacks down to MessageBubble;
    │                                  # wires the one-click continuous-conversation handler
    └── ChatPage.test.tsx             # MODIFIED
```

**Structure Decision**: Single frontend feature-domain (`features/chat`), following the
project's existing "organized by feature-domain under `src/features/<domain>`" convention
(constitution §4 Folder structure). No new top-level directories, no new shared primitives
under `src/shared` — every changed file already exists in this domain. No backend project is
affected, so the "Web application (frontend + backend)" structure option applies only in the
sense that a backend exists in the repo; this feature's own diff is frontend-only.

## Complexity Tracking

*No violations — table intentionally empty.*
