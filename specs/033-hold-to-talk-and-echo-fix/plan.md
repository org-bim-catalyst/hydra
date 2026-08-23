# Implementation Plan: Hold-to-Talk Simplification & Self-Listening Fix

**Branch**: `033-hold-to-talk-and-echo-fix` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/033-hold-to-talk-and-echo-fix/spec.md`

## Summary

Close the last unclassified failure mode behind the still-reproducing production transcription
500 (a malformed 2xx response from OpenAI, reusing `AiProviderUnavailableException`) and, this
time, actually commit/merge/deploy the fix — SPEC-032's own fix was left uncommitted, which is
part of why the 500 is still occurring. Simplify Push-to-Talk to a single, root-caused gesture:
press-and-hold always records, release always stops-and-transcribes, fixing a real bug (missing
`setPointerCapture` lets a DOM swap steal the release event) rather than just changing stated
behavior; the mid-recording Cancel button is removed from this gesture (unreachable once release
always finishes) but the Collapsed widget's separate, unrelated click-to-toggle Push-to-Talk flow
is untouched. Fully mute the microphone input during Lucy's spoken replies in Continuous mode
(toggling `MediaStreamTrack.enabled`, no audio-graph teardown), deliberately removing the existing
mid-response interruption feature per the resolved clarification, and removing the now-dead
ducking/interruption code that feature leaves behind.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript 5.x / React 19 (frontend)

**Primary Dependencies**: Backend — the existing `IAIProvider`/`AiProvider*Exception` abstraction
(no new dependency; reuses `AiProviderUnavailableException`). Frontend — MUI (`IconButton`,
`VoiceAnalyzer`), the Pointer Events API (`setPointerCapture`), `MediaStreamTrack.enabled`,
`getUserMedia`'s `echoCancellation` constraint — all existing web platform APIs, no new packages.

**Storage**: N/A — no schema/data changes.

**Testing**: xUnit + FluentAssertions (backend, extending SPEC-032's own new
`OpenAIProviderTests.cs` — no placement conflict since that file is this session's own recent
addition, per research.md Decision 5), Vitest + React Testing Library (frontend, existing
conventions).

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`) + React SPA (`AskLucy.Web/ClientApp`).

**Project Type**: Web application — full-stack fix within the existing Clean Architecture
solution, continuing the specs/029-032 chat-widget bug-fix series.

**Performance Goals**: No new latency in the success path. Muting via `track.enabled` avoids
audio-graph/WebSocket reconnect overhead on every conversation turn (FR-010's "no noticeable added
delay" requirement).

**Constraints**: Must not alter SPEC-032's already-correct 400/401/403/429/5xx classification or
retry behavior (spec.md FR-011). Must not affect `CollapsedVoiceControls.tsx`'s independent,
unrelated click-to-toggle Push-to-Talk flow (research.md Decision 3's scope correction). This
feature's own `/speckit-cicd` pass is a functional requirement (FR-004), not optional process —
the fix must be verifiably committed, merged, and deployed, closing the exact gap that left
SPEC-032 uncommitted.

**Scale/Scope**: One new backend exception-handling branch (reusing an existing type, no new
type); one frontend gesture-handling rewrite (`ChatComposer.tsx`) removing more code than it adds;
one frontend audio-muting addition (`useSpeechRecognition.ts`/`useConversationAudio.ts`) that also
removes now-dead interruption code. No new endpoints, no new pages, no database changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| §1 Clean Architecture / Dependency Rule | PASS | Reuses `AiProviderUnavailableException` (already in `AskLucy.Application.Abstractions`) — no new cross-layer dependency introduced. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | PASS | Closes a second, previously-unidentified silent-failure gap (a malformed 2xx response falling through to a generic 500) — the same principle SPEC-032 was driven by, now applied to the one remaining unclassified path in the same method. |
| §3 Simplicity / DRY / YAGNI | PASS | Reuses an existing exception type rather than adding a fourth variant. Removes more frontend code than it adds (tap/hold duration logic, `RecordingReviewControls` rendering in `ChatComposer`, the now-dead interruption/ducking machinery) — a net simplification, not a new abstraction. |
| §6 API Standards — Problem Details | PASS | The malformed-response case reuses the existing `AiProviderUnavailableException` → 502 Problem Details mapping — no new response shape. |
| §7 UI Principles — accessibility, design system reuse | PASS | `setPointerCapture` and the always-mounted mic button are a robustness fix, not a new component; existing `Tooltip`/`IconButton`/`VoiceAnalyzer` reused as-is. No new UI surface. |
| §9 AI Principles — provider abstraction | PASS | No provider-specific logic added outside `OpenAIProvider.cs`'s existing scope; the fix is a generic "malformed response" handler, not an OpenAI-specific workaround leaking into `Application`. |
| §10 Testing Standards | PASS (planned in tasks) | New xUnit test for the malformed-2xx-response case; updated `ChatComposer.test.tsx` for the single-gesture behavior and pointer-capture expectations; new/updated `useConversationAudio.test.ts` assertions for mute-on-`AiSpeaking`/unmute-after and the absence of any `'Interrupted'` transition. |
| §11 Git Workflow / §12 CI/CD | PASS (planned, elevated to a functional requirement) | FR-004/SC-003 make "actually committed, merged, and deployed" an explicit acceptance criterion for this feature — directly closing the process gap SPEC-032 left open. |
| §16 Quality Gates | PASS (planned) | No architecture violations; tests accompany every behavior change; no accessibility regression (same interactive elements, no new/removed labels beyond what's already removed alongside the buttons themselves). |

No violations identified — Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/033-hold-to-talk-and-echo-fix/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Infrastructure/
│   └── Ai/
│       └── OpenAIProvider.cs                          # MODIFIED: catch malformed-2xx in TranscribeAudioAsync
└── AskLucy.Web/
    └── ClientApp/src/
        └── features/chat/
            ├── components/
            │   ├── ChatComposer.tsx                    # MODIFIED: single hold gesture, setPointerCapture, no RecordingReviewControls
            │   └── ChatComposer.test.tsx                # MODIFIED
            └── voice/
                ├── useSpeechRecognition.ts              # MODIFIED: setInputMuted, echoCancellation constraint
                ├── useConversationAudio.ts              # MODIFIED: mute/unmute on AiSpeaking, remove dead ducking code
                └── useConversationAudio.test.ts         # MODIFIED
tests/
└── AskLucy.Infrastructure.Tests/
    └── Ai/
        └── OpenAIProviderTests.cs                       # MODIFIED (this session's own file — no placement conflict, research.md Decision 5)
```

**Structure Decision**: Extend `OpenAIProvider.cs`'s existing exception-handling pattern in place
(backend). On the frontend, rewrite `ChatComposer.tsx`'s gesture-handling section (net code
reduction) and extend `useSpeechRecognition.ts`/`useConversationAudio.ts` with the muting
mechanism while removing the now-dead interruption code they currently contain. No new files
beyond this feature's own spec-kit artifacts — `RecordingReviewControls.tsx`,
`CollapsedVoiceControls.tsx`, `useVoiceRecorder.ts`, and `ChatPage.tsx` are explicitly **not**
touched (research.md Decision 3's scope correction).

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
