# Implementation Plan: Transcription Crash Fix, Review-Gesture Restoration & Continuous Voice View

**Branch**: `034-transcription-crash-gesture-and-continuous-view` | **Date**: 2026-08-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/034-transcription-crash-gesture-and-continuous-view/spec.md`

## Summary

Close the actual third cause behind the transcription-500 that survived two prior fix rounds — a
null `IFormFile` binding on a malformed/missing multipart upload throwing an uncaught
`NullReferenceException` in `AiController.cs`, entirely outside `OpenAIProvider.cs` where both
prior fixes lived — and fix production logging (currently a silent void: Console-only Serilog sink
with IIS/ANCM stdout capture disabled) so this can never again be an undiagnosable repeat.
Restore Push-to-Talk's dual gesture in `ChatComposer.tsx`: a tap starts recording and shows
explicit confirm/discard controls (reusing `CollapsedVoiceControls`' already-correct
`RecordingReviewControls` pattern) while a hold shows only the waveform and auto-completes on
release (keeping specs/033's real `setPointerCapture` bug fix unchanged). Restructure Continuous
mode into a dedicated, focused voice view (Exit + Mute only) — and, in the process, correct a
specs/033 miss: its mic-mute fix was built into `useConversationAudio.ts`, a hook never actually
rendered anywhere; the new dedicated view is where it finally gets wired up for real, replacing
`ChatPage.tsx`'s separate, parallel inline Continuous-mode implementation rather than leaving two
duplicate orchestrations in the codebase.

## Technical Context

**Language/Version**: C# / .NET 10 (backend), TypeScript 5.x / React 19 (frontend)

**Primary Dependencies**: Backend — adds `Serilog.Sinks.File` (new NuGet package) for Decision 2;
otherwise the existing ASP.NET Core MVC model-binding/`ProblemDetails` conventions. Frontend — no
new packages; reuses `RecordingReviewControls`, `AiPresenceCard`/`SceneBackground`, and
`useConversationAudio` (already built in specs/012/033, previously unwired).

**Storage**: N/A — no schema/data changes. Adds a rolling log *file* on the deployed host
(operational, not application data).

**Testing**: xUnit + FluentAssertions (backend — new controller-level guard tests), Vitest + React
Testing Library (frontend — `ChatComposer.test.tsx` gesture rewrite, a new test file for the
dedicated voice view component, `ChatPage.test.tsx` updates for the removed inline Continuous
plumbing).

**Target Platform**: ASP.NET Core Web API (`AskLucy.Web`, IIS/ANCM out-of-process on
`site4now.net`) + React SPA (`AskLucy.Web/ClientApp`).

**Project Type**: Web application — full-stack fix continuing the specs/029-033 chat-widget
bug-fix series; the first round in this series to touch backend hosting/logging configuration
(Decision 2) and to retire a previously-dead frontend hook by finally wiring it up (Decision 4).

**Performance Goals**: No new latency in the success path for transcription (a guard clause on an
already-failing case). The dedicated voice view's audio graph setup cost is the same
`useConversationAudio`/`useSpeechRecognition` cost already paid by the code path it replaces — not
a new cost, a relocated one.

**Constraints**: Must not alter specs/032/033's already-correct classification (400/401/403/429/
5xx/malformed-2xx) — Decision 1's guard sits entirely upstream of that logic. Must preserve
specs/033's `setPointerCapture` fix exactly (Decision 3 changes only what happens at release, not
how the press itself is captured). Must not leave two parallel Continuous-mode implementations
after this ships (Decision 4) — the old inline path is removed, not left as a disabled fallback.

**Scale/Scope**: Backend: one controller guard (two call sites) + one logging-config change +
one new NuGet package. Frontend: one gesture-handling restoration in `ChatComposer.tsx` (net
addition, reusing an existing component), one new dedicated-view component, and removal of
~50 lines of now-superseded inline Continuous-mode logic from `ChatPage.tsx`. No new endpoints,
no database changes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| §1 Clean Architecture / Dependency Rule | PASS | The controller guard stays in `AskLucy.Web` (Presentation layer, where `IFormFile` model binding already lives) — no new cross-layer dependency. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | PASS | This feature exists specifically to close a silent-failure gap two prior rounds each partially addressed; Decision 2 (logging) is itself a direct application of this principle — a failure isn't "surfaced" if it's unobservable in production. |
| §3 Simplicity / DRY / YAGNI | PASS | Decision 3 reuses `RecordingReviewControls` (no new component). Decision 4 removes a duplicate implementation rather than adding a second parallel one — a net simplification despite the new view. |
| §6 API Standards — Problem Details | PASS | The new guard returns a standard `ProblemDetails` 400, consistent with the existing convention; no ad hoc shape. |
| §7 UI Principles — design system reuse | PASS | Decision 5 reuses `SceneBackground`/`AiPresenceCard`'s existing visualization rather than building a new one. |
| §9 AI Principles — Observability | PASS | Decision 2 directly serves "every AI call is traceable end-to-end" — currently untrue in production for any unhandled exception. |
| §10 Testing Standards | PASS (planned in tasks) | New backend test for the upload guard; frontend gesture tests rewritten for the dual-mode behavior; new tests for the dedicated view component; `ChatPage.test.tsx` updated for the removed inline plumbing. |
| §14 Observability | PASS | Decision 2 is this gate's direct subject — fixes a real, confirmed gap (Console-only sink, stdout capture disabled). |
| §16 Quality Gates | PASS (planned) | No architecture violations; tests accompany every behavior change; accessibility carried over for the new view (keyboard-operable Exit/Mute, visible focus states, ARIA labels) per §7. |

No violations identified — Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/034-transcription-crash-gesture-and-continuous-view/
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
├── AskLucy.Web/
│   ├── Controllers/v1/
│   │   └── AiController.cs                       # MODIFIED: null/empty-file guard on Transcribe/TranscribeMicrophone
│   ├── AskLucy.Web.csproj                        # MODIFIED: add Serilog.Sinks.File, StdoutLogEnabled
│   ├── appsettings.Production.json               # MODIFIED: Serilog:WriteTo file sink
│   └── ClientApp/src/
│       └── features/chat/
│           ├── components/
│           │   ├── ChatComposer.tsx               # MODIFIED: restore tap/hold dual gesture
│           │   ├── ChatComposer.test.tsx          # MODIFIED
│           │   ├── ContinuousVoiceView.tsx         # NEW: dedicated Continuous-mode view
│           │   └── ContinuousVoiceView.test.tsx    # NEW
│           └── pages/
│               ├── ChatPage.tsx                    # MODIFIED: render ContinuousVoiceView, remove superseded inline Continuous plumbing
│               ├── ChatPage.test.tsx               # MODIFIED
│               └── ChatPage.a11y.test.tsx          # MODIFIED
tests/
└── AskLucy.Web.Tests/
    └── Ai/
        └── TranscriptionUploadGuardTests.cs        # NEW: null/empty-file guard test
```

**Structure Decision**: Backend guard added directly to `AiController.cs` (Presentation layer,
matching where the gap was found); logging config changed at the two files that actually govern
it (`.csproj`, `appsettings.Production.json`). Frontend: `ChatComposer.tsx` restoration reuses the
existing `RecordingReviewControls` import path (previously removed in specs/033, reinstated here);
one genuinely new component (`ContinuousVoiceView.tsx`) built on the already-existing
`useConversationAudio`/`AiPresenceCard`/`SceneBackground` — no new hooks or 3D visualization code.
`ChatPage.tsx` loses its old inline Continuous-mode wiring in the same change that adds the new
view, avoiding a transitional state with two implementations.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
