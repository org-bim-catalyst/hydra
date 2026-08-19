# Implementation Plan: Floating Chat Assistant Redesign

**Branch**: `026-floating-chat-assistant` | **Date**: 2026-08-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/026-floating-chat-assistant/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

Redesign the Studio workspace's chat entry point as a bespoke, always-visible floating widget with two states — a narrow Collapsed vertical strip (handle, real-time voice analyzer, Push-to-Talk, Continuous Listening toggle, Mute Agent, status indicator) and an Expanded conversation panel — replacing today's generic circular-icon chat trigger. Alongside the redesign: remove the "+ New chat" text button (auto-start a new conversation each session, with a minimal icon-only manual option retained in the Expanded header), remove the inline language dropdown in favor of a read-only circular flag driven by a new Chat Configuration setting, remove the standalone "Generate image" button, and replace Push-to-Talk's live-transcription capture with a discrete record → review (waveform, no live transcript) → cancel/accept-to-transcribe flow, leaving Continuous Listening untouched. No underlying chat/AI/LLM behavior changes — this is presentation and interaction-model only (FR-026/FR-027).

Technical approach: a new client-side `ChatAssistantWidget` component tree lives outside the existing `CircularAction`/`WorkspaceOverlay` control system (which stays unchanged for the other six Studio controls) but continues to coordinate through the same `workspaceOverlayStore` for mutual-exclusivity; a new `useVoiceRecorder` hook (built on `MediaRecorder` + `AnalyserNode`, not the existing realtime-streaming `useSpeechRecognition`) drives Push-to-Talk's local-buffer-then-explicit-send flow, reusing the existing `/ai/transcriptions` REST endpoint; the new default-language preference extends the existing `UserVoicePreference` entity/API with one nullable field.

## Technical Context

**Language/Version**: TypeScript 5 / React 19 (frontend, `src/AskLucy.Web/ClientApp`); C# / .NET 10 (backend, `src/AskLucy.*`)

**Primary Dependencies**: MUI, Zustand, TanStack Query, `@remixicon/react` (already added this session) — frontend; ASP.NET Core, EF Core, MediatR, FluentValidation — backend. No new npm/NuGet dependency required by this feature: Push-to-Talk's recording/waveform uses only browser-native `MediaRecorder`/`AudioContext`/`AnalyserNode` APIs (research.md #2/#3), matching the pattern `useVoiceAnalyzer.ts` already establishes.

**Storage**: SQL Server via EF Core — one additive nullable column (`UserVoicePreference.DefaultLanguage`) on an existing table; no other schema change (data-model.md).

**Testing**: Vitest + Testing Library + jest-axe (frontend, existing convention); xUnit (backend, existing convention) — constitution §10.

**Target Platform**: Web — existing React SPA (`AskLucy.Web/ClientApp`) served by the existing ASP.NET Core Web API. Modern desktop and mobile browsers supporting `getUserMedia`/`MediaRecorder`/`AudioContext` (same baseline `useSpeechRecognition`/`useVoiceAnalyzer` already require).

**Project Type**: Web application — existing `src/AskLucy.Web/ClientApp` (frontend) + `src/AskLucy.{Domain,Application,Infrastructure*,Persistence,Web}` (backend, Clean Architecture layers per constitution §3). No new project/layer is introduced.

**Performance Goals**: Collapsed↔Expanded transition perceived as smooth with no visible stutter, matching spec 024's established ~300ms precedent (SC-002); waveform/analyzer visuals update at animation-frame rate via `requestAnimationFrame` polling of ref-based intensity getters (research.md #3), never via per-frame React state.

**Constraints**: No audio is transmitted to any transcription service before the user's explicit accept action during Push-to-Talk recording (FR-019/FR-021/FR-022 — a hard privacy-by-design requirement verified in quickstart.md Scenario 5 via network-panel inspection); reduced-motion honored for all new transitions (FR-009); every control operable via mouse, touch, and keyboard alone (FR-010); zero change to AI/LLM request handling, streaming, persistence, or provider/model selection (FR-026).

**Scale/Scope**: One Studio page; five supported response languages initially (`en`/`ar`/`es`/`fr`/`de`, the existing `LanguageSelector.tsx` set); no new multi-tenant, throughput, or storage-scale concerns beyond the existing app.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | PASS | Backend change is additive within the existing Ai vertical slice (`Domain` → `Application` → `Infrastructure`/`Persistence` → `Api`); no new cross-layer dependency introduced. |
| II. SOLID | PASS | New frontend units are each single-purpose (`ChatAssistantWidget` composes `CollapsedChatControl`/`ExpandedChatPanel`/`VoiceAnalyzer`; `useVoiceRecorder` owns only recording state). |
| III. Simplicity First (DRY/KISS/YAGNI) | PASS | Reuses existing `transcribeAudio` endpoint, existing `UserVoicePreference` entity, and existing `VoiceControlBar` prop contract instead of introducing parallel mechanisms — each explicitly justified in research.md (#2, #4, #10) over a "build new" alternative. |
| IV. Composition Over Inheritance | PASS | No inheritance introduced. |
| V. Dependency Inversion & Testability | PASS | New backend field flows through the existing MediatR command/query/validator seam; new frontend hook (`useVoiceRecorder`) is a plain, independently unit-testable hook with browser APIs as its only "dependency," mockable in tests. |
| VI. Separation of Concerns | PASS | Presentation components stay presentation-only; the language allow-list validation lives in `SaveUserVoicePreferenceCommandValidator` (Application layer), not a controller or a React component. |
| VII. Convention Over Configuration | PASS | Extends the existing `UserVoicePreference` vertical slice (research.md #4) rather than inventing a new preference concept for one field. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | PASS | `useVoiceRecorder`'s `error` surfaces via the same `Snackbar`/`Alert` pattern already used for `recognition.error`; `defaultLanguage` save failures reuse `useVoicePreferencesStore`'s existing rollback-and-surface behavior — both enumerated explicitly in data-model.md/quickstart.md. |
| §7 UI Principles — Accessibility | REVIEWED, no violation | `ChatAssistantWidget` is deliberately taken off the already-audited `CircularAction` path (research.md #1) to satisfy the spec's required Collapsed shape; research.md #9 commits it to independently re-implementing the same WAI-ARIA disclosure contract plus its own `*.a11y.test.tsx` coverage, since it does not inherit `CircularAction`'s tests for free. Flagged here, not in Complexity Tracking, because no principle is being bent — it's new coverage, not skipped coverage. |
| §7 UI Principles — Theming/Design system | PASS | Reuses `CIRCULAR_ACTION_CHROME`'s existing dark-glass token family (research.md #8) rather than a second palette. |
| §7 UI Principles — Internationalization | PASS (distinct concept) | `defaultLanguage` is a response-language preference (existing product concept, already present via `LanguageSelector`), not a UI-string localization framework — does not engage the constitution's "no i18n framework yet" clause. |
| §7 UI Principles — Responsive design | ADDRESSED (research.md #11) | `ChatAssistantWidget` is deliberately built outside `FloatingToolbar`/`WorkspaceOverlay`'s already-responsive control grouping (research.md #1), so it cannot inherit that system's mobile/tablet/desktop behavior for free — research.md #11 defines its own responsive strategy (fixed-edge anchoring + MUI breakpoint-driven sizing), verified in tasks.md T056 and quickstart.md. Flagged explicitly per `/speckit-analyze` finding E2. |
| §8 Security | PASS | Reuses existing authorization on `/ai/transcriptions` and `/ai/voice/preferences`; the redesigned flow's core property (no audio leaves the client until explicit accept) is a *strengthening* of the previous implicit-streaming behavior, not a weakening. |
| §10 Testing Standards | PASS (planned) | Unit tests for `useVoiceRecorder`; component + a11y tests for the three new widget components; backend validator/unit/integration tests for the extended preference field — enumerated in quickstart.md, to be broken into tasks by `/speckit-tasks`. |

No violations requiring the Complexity Tracking table — it is left empty.

## Project Structure

### Documentation (this feature)

```text
specs/026-floating-chat-assistant/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md         # Phase 1 output (/speckit-plan command)
├── contracts/            # Phase 1 output (/speckit-plan command)
│   ├── chat-widget-components.md
│   └── voice-preference-api.md
└── tasks.md              # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/Ai/
└── UserVoicePreference.cs                       # + DefaultLanguage field (data-model.md)

src/AskLucy.Application/Ai/
├── Commands/SaveUserVoicePreference/             # + defaultLanguage param + validator rule
├── Queries/GetUserVoicePreference/                # unchanged shape, DTO gains the field
└── UserVoicePreferenceDto.cs                      # + DefaultLanguage

src/AskLucy.Persistence/
├── Configurations/UserVoicePreferenceConfiguration.cs   # + column mapping
├── Repositories/UserVoicePreferenceRepository.cs         # unchanged
└── Migrations/                                    # + one additive migration

src/AskLucy.Web/Controllers/v1/AiController.cs      # request/response DTOs gain the field only

src/AskLucy.Web/ClientApp/src/
├── features/chat/
│   ├── components/
│   │   ├── ChatAssistantWidget.tsx                # NEW — top-level widget (research.md #1)
│   │   ├── CollapsedChatControl.tsx                # NEW
│   │   ├── ExpandedChatPanel.tsx                   # NEW — absorbs AssistantPanel's role
│   │   ├── VoiceAnalyzer.tsx                       # NEW
│   │   ├── CollapsedVoiceControls.tsx              # NEW — vertical layout sharing VoiceControlBar's prop contract (research.md #10)
│   │   ├── ActiveLanguageFlag.tsx                  # NEW
│   │   ├── VoiceControlBar.tsx                     # extended: shared prop contract + recording-review UI (research.md #10)
│   │   ├── AssistantPanel.tsx                      # REMOVED
│   │   └── LanguageSelector.tsx                    # REMOVED
│   ├── languageOptions.ts                          # NEW — shared SUPPORTED_LANGUAGES + LANGUAGE_FLAGS (research.md #6)
│   ├── voice/
│   │   ├── useVoiceRecorder.ts                     # NEW (research.md #2)
│   │   └── useSpeechRecognition.ts                 # unchanged, scoped to Continuous only
│   ├── api/aiApi.ts                                # unchanged — transcribeAudio reused as-is
│   ├── api/voiceApi.ts                             # + defaultLanguage field
│   ├── voice/voicePreferencesStore.ts              # + defaultLanguage field
│   ├── pages/ChatPage.tsx                          # composes ChatAssistantWidget in place of the old chat ControlDefinition
│   └── workspaceControls.tsx                       # unchanged (view-mode/layers/navigation/selection/analysis/account)
└── features/settings/pages/ChatConfigurationTab.tsx # + "Default language" control
```

**Structure Decision**: No new top-level project or directory is introduced. All frontend work lands inside the existing `src/AskLucy.Web/ClientApp/src/features/chat/` feature folder (new widget components alongside existing `ChatComposer.tsx`/`MessageBubble.tsx`/etc., new hook alongside `useSpeechRecognition.ts`), following the constitution's §4 "organized by feature-domain under `src/features/<domain>`" convention exactly as spec 024/025 already did. The one Settings-side change (`ChatConfigurationTab.tsx`) follows that same existing file. All backend work extends the existing `Ai` vertical slice already used by `UserVoicePreference`, following constitution §3/§4's layer-then-feature convention — no new backend project, controller, or CQRS module.
