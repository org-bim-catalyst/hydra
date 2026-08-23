# Implementation Plan: Voice Controls & Composer Redesign

**Branch**: `031-voice-controls-redesign` | **Date**: 2026-08-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/031-voice-controls-redesign/spec.md`

## Summary

Fix the Push-to-Talk recording flow so both the tap-then-finish and hold-then-release gestures
transcribe and populate the message field in a single step (removing the confusing manual
"send for transcription" button that today sits between `finish()` and `accept()`), declutter
the composer footer to show only recording-relevant controls while a Push-to-Talk recording is
active, delete the translate feature entirely, and relocate the mute/unmute-Lucy control from
the composer footer into the `ExpandedChatPanel` header next to Lucy's portrait. All grounded in
the current code (verified by reading `ChatComposer.tsx`, `ExpandedChatPanel.tsx`,
`useVoiceRecorder.ts`, `RecordingReviewControls.tsx`, `CollapsedVoiceControls.tsx`,
`ChatPage.tsx`, `useChatStream.ts` before planning).

## Technical Context

**Language/Version**: TypeScript 5.x, React 19

**Primary Dependencies**: Material UI (MUI) v6/v7, Zustand 5, `@remixicon/react`, the existing
`MediaRecorder`/`Web Audio` browser APIs already used by `useVoiceRecorder.ts`

**Storage**: N/A — no new persisted state; this feature changes control flow and layout only

**Testing**: Vitest + React Testing Library, existing `*.a11y.test.tsx` convention

**Target Platform**: Web (SPA), `src/AskLucy.Web/ClientApp`

**Project Type**: Web application — frontend-only within the existing React SPA; no backend
changes (the reported "Transcription failed with 500" is an existing backend/runtime concern,
explicitly out of scope per spec.md's Assumptions)

**Performance Goals**: The auto-transcribe-on-finish/release path must feel instantaneous to the
user (no added round-trip beyond the existing `transcribeAudio` call) — this feature removes a
manual confirmation step, it does not add one.

**Constraints**: Must preserve all underlying voice/recording/mute state management
(`useSpeechRecognition`, `useVoiceRecorder`, `useVoiceOutput`) — only which UI exposes each
control and how the transcription result reaches the text field change (spec.md's own
constraint). Must not silently drop the reported "attach only supports audio" concern without
investigation (FR-013). Must satisfy constitution §7 accessibility/responsive/state-management
rules and §18's "never invent requirements not present in the approved specification."

**Scale/Scope**: Six files modified in the primary chat feature area
(`ChatComposer.tsx`, `ExpandedChatPanel.tsx`, `ChatPage.tsx`, `useVoiceRecorder.ts`,
`RecordingReviewControls.tsx`, `useChatStream.ts`), one file (`CollapsedVoiceControls.tsx`)
benefiting automatically from the shared `RecordingReviewControls`/`useVoiceRecorder` fix with no
direct edit required, plus associated test updates. No new files, no backend/API changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

Frontend-only interaction/UI change — architecture-layer gates (§1–§6, §8, §9) are **N/A** (no
Domain/Application/Infrastructure changes, no new API endpoint, no AI/provider change).

Applicable gates:

| Gate | Status | Notes |
|------|--------|-------|
| §7 State management (Zustand for client/UI state, no duplication into TanStack Query) | PASS | No new state store introduced; existing `useVoiceRecorder`/`useSpeechRecognition`/`useVoiceOutput` hooks and `voicePreferencesStore` are reused as-is, per spec.md's explicit constraint. |
| §7 Design system / component reuse | PASS | No new shared component; `RecordingReviewControls` (already shared by `ChatComposer` and `CollapsedVoiceControls`) is simplified, not duplicated — the fix benefits both surfaces from one change (DRY, §3). |
| §7 Accessibility (WCAG 2.1 AA) | PASS (verify in Phase 1/tasks) | Existing `aria-label`/`Tooltip` patterns from specs/029/030 are preserved and extended to the relocated mute control and the simplified recording-review controls. |
| §3 Simplicity / DRY / YAGNI | PASS | Removes now-dead code (`'reviewing'` phase, the manual review-step Accept button, `sendTranslation` and its call sites) rather than leaving it stubbed out — a real cleanup, not scope creep, since it's the direct cause of the reported bug. |
| §10 Testing (component tests for changed behavior) | PASS (planned in tasks) | Updated Vitest+RTL coverage for `ChatComposer`, `ExpandedChatPanel`, `useVoiceRecorder`, `RecordingReviewControls`, `CollapsedVoiceControls` (regression check only, no direct edit), `ChatPage`. |
| §18 No invented requirements | PASS | Every functional requirement traces to the user's own testing feedback (spec.md's Input); two residual ambiguities (short-hold discard logic, mid-hold cancel) were resolved via the simplest available default — reusing existing behavior rather than inventing new mechanisms — documented in spec.md's Assumptions and research.md. |

No violations identified — Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/031-voice-controls-redesign/
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
│   ├── voice/
│   │   ├── useVoiceRecorder.ts           # MODIFIED: remove 'reviewing' phase; finish()
│   │   │                                   becomes async and transcribes directly
│   │   └── useVoiceRecorder.test.ts      # MODIFIED (if exists) / verified
│   ├── components/
│   │   ├── RecordingReviewControls.tsx   # MODIFIED: drop the phase==='reviewing' Accept
│   │   │                                   button (dead after the hook change); onAccept
│   │   │                                   prop removed
│   │   ├── RecordingReviewControls.test.tsx  # MODIFIED
│   │   ├── ChatComposer.tsx              # MODIFIED: remove translate control; hide
│   │   │                                   non-recording footer controls while a PTT
│   │   │                                   recording is active; remove onTranslateLastClick/
│   │   │                                   isMuted/onToggleMute props (mute moves to panel
│   │   │                                   header)
│   │   ├── ChatComposer.test.tsx         # MODIFIED
│   │   ├── ExpandedChatPanel.tsx         # MODIFIED: add mute/unmute-Lucy control next to
│   │   │                                   LucyPortrait in the header
│   │   ├── ExpandedChatPanel.test.tsx    # MODIFIED
│   │   ├── ExpandedChatPanel.a11y.test.tsx # MODIFIED
│   │   └── CollapsedVoiceControls.tsx    # NOT directly edited — inherits the
│   │                                       RecordingReviewControls/useVoiceRecorder fix
│   │                                       automatically; its tests are re-run to confirm
│   │                                       no regression, not edited
│   ├── hooks/
│   │   └── useChatStream.ts              # MODIFIED: remove sendTranslation (dead after
│   │                                       ChatPage.tsx's call site is removed)
│   │   └── useChatStream.test.ts         # MODIFIED (if exists)
│   └── pages/
│       ├── ChatPage.tsx                  # MODIFIED: remove handleTranslateLast and its
│       │                                   wiring; replace split onFinish/onAccept wiring
│       │                                   with a single auto-transcribing handler; wire
│       │                                   isMuted/onToggleMute into ExpandedChatPanel
│       │                                   instead of ChatComposer
│       ├── ChatPage.test.tsx             # MODIFIED
│       └── ChatPage.a11y.test.tsx        # MODIFIED (if it exercises translate/mute)
```

**Structure Decision**: Extend the existing files in place — no new components or stores. The
one cross-cutting change (`useVoiceRecorder.ts`'s phase simplification) is made once at the
shared-hook level so both `ChatComposer` (Expanded panel, this feature's primary target) and
`CollapsedVoiceControls` (Collapsed widget, not in this feature's explicit scope but sharing the
same `RecordingReviewControls` component) get the bug fix from one change, per constitution §3
DRY — re-verified via its existing test suite, not re-implemented.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
