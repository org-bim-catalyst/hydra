# Implementation Plan: ElevenLabs Conversational Voice Engine

**Branch**: `012-elevenlabs-voice-engine` | **Date**: 2026-08-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/012-elevenlabs-voice-engine/spec.md`

## Summary

Replace today's voice stack — self-hosted Whisper.net batch transcription and the browser's
native `speechSynthesis` TTS — with ElevenLabs' streaming Speech-to-Text and Text-to-Speech
APIs, and layer a hands-free Continuous Conversation Mode with natural interruption on top,
while keeping the existing Push-to-Talk interaction pattern. The current Whisper/
`speechSynthesis` implementation is **not deleted**: per the spec's Clarifications it becomes
a permanent, automatically-triggered fallback when ElevenLabs is unreachable, with automatic
recovery back to the primary provider and admin-visible failover signals.

Architecturally, this is additive: a new `ISpeechToTextSessionProvider` / `ITextToSpeechProvider`
pair of Application abstractions (constitution §9, mirroring the existing `IAIProvider`/
`ITranscriptionProvider` pattern) with ElevenLabs implementations in `Infrastructure.Ai`, a
new orchestration endpoint that streams the LLM's reply and its synthesized speech together
(so audio starts before the text response finishes generating, per SC-001), and a frontend
voice module that feeds the *existing, unmodified* Three.js sphere real audio-derived
reactivity instead of the current timing-based approximation. No existing chat, RAG, or
multi-provider-AI-engine code changes; no new third-party packages are required — every new
capability is built on `HttpClientFactory`, the browser's native `WebSocket`, and the Web
Audio API, all already present in the stack.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend, existing solution); TypeScript 5.x / React 19
(frontend, existing `ClientApp`). No new language.

**Primary Dependencies**: No new NuGet or npm packages. Backend: existing
`IHttpClientFactory` (same hand-rolled-HTTP convention as `OpenAIProvider`/
`AnthropicProvider` — no ElevenLabs SDK), `MediatR`, `FluentValidation`, EF Core, ASP.NET
Data Protection, Serilog. Frontend: existing `@react-three/fiber`/`three`, `zustand`, native
browser `WebSocket` and Web Audio API (`AudioContext`/`AnalyserNode`) — no client SDK.

**Storage**: SQL Server via EF Core (existing `AskLucy.Persistence`) — two new small tables
(`UserVoicePreference`, `VoiceProviderFailoverEvent`); voice turn transcripts/replies persist
through the *existing* `Message`/`UserChat` tables, unchanged.

**Testing**: xUnit (backend, existing `tests/AskLucy.*.Tests` projects) with fakes/recorded
HTTP fixtures for the new ElevenLabs adapters (constitution §10 — no live vendor calls in
CI); Vitest + React Testing Library + MSW + jest-axe (frontend, existing `ClientApp`
tooling) for the new hooks, state machine, and voice control UI.

**Target Platform**: ASP.NET Core 10 hosted on the existing myASP.NET Windows/IIS (ANCM)
deployment; React SPA static build served the same way it is today. No new hosting
capability is required — see research.md Decision 2/3 for why the design deliberately avoids
depending on unconfirmed inbound WebSocket support on this host.

**Project Type**: Web application (existing two-part layout: layered .NET backend +
React SPA) — extends the existing structure, introduces no new project.

**Performance Goals**: Directly from spec.md Success Criteria — first audio within 2s of the
AI starting to respond (SC-001); interruption stops playback within 300ms (SC-002); Continuous
Mode re-listens within 1s of the AI finishing (SC-003); mute takes effect within 200ms
(SC-007); auto-recovery back to the primary provider completes by the next turn (SC-010).
The existing sphere's 60fps rendering budget (established in specs/011-particle-sphere-engine)
must not regress — this feature only changes what feeds `getReactiveIntensity()`, not how the
sphere renders.

**Constraints**: ElevenLabs API key never reaches the browser (constitution §8) — the browser
only ever receives a short-lived, single-use STT session token it cannot use for anything but
listening on one session (same class of pattern as the existing `SignedUrlService` for direct,
time-limited client access to file storage). MyASP.NET's inbound WebSocket support is
unconfirmed for this host (research.md) — the design avoids requiring it. New endpoints join
the existing `[EnableRateLimiting("ai-endpoints")]` policy; no new rate-limit policy.

**Scale/Scope**: All authenticated users at launch (FR-040, Clarifications) — no tier gating,
no new numeric scale target beyond the platform's existing capacity.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design — see "Post-Design
Re-check" below.*

| Principle / Gate | Status | Notes |
|---|---|---|
| §3 Clean Architecture & Dependency Rule | PASS | `ISpeechToTextSessionProvider`, `ITextToSpeechProvider`, `IVoiceProviderHealthRecorder` live in `Application/Abstractions`; ElevenLabs implementations live in `Infrastructure.Ai`. No Domain/Application code references ElevenLabs wire formats. |
| §9 Provider abstraction & explicit fallback policy | PASS | Spec FR-033–FR-037 *is* the "fallback provider/model policy for outage conditions" §9 requires, applied through the abstraction (health-checked switch), not ad hoc try/catch per call site. |
| §6 Streaming & cancellation | PASS | New `/api/v1/ai/voice/reply` reuses the existing chunked-transfer streaming convention and the existing `CancellationToken`-propagation pattern already proven by `/api/v1/ai/chat`. |
| §6 Token/cost-based throttling for AI-invoking endpoints | **Deferred, tracked** | Constitution §6 additionally requires AI-invoking endpoints to enforce token/cost-based throttling, not just request-count throttling (research.md Decision 7 only added the new voice endpoints to the existing request-count `ai-endpoints` policy). This mirrors an already-accepted, platform-wide gap specs/005-multi-provider-ai-engine documented and deferred to a future Billing Engine spec — not a new violation introduced here, but distinct enough from the streaming/cancellation row above that it must not be silently folded into that PASS (`/speckit-analyze` finding C1/C2). tasks.md T084 adds the matching Assumption to spec.md. |
| §7 UI — voice persona consistency | PASS | FR-009 was amended during `/speckit-clarify` to explicitly require persona consistency in fallback mode too; the fallback engine already satisfies this today (`voicePersonaMap.ts`/`selectPersonaVoice.ts`, unchanged). |
| §8 Security — secrets, least privilege | PASS | `ElevenLabsOptions.ApiKey` bound via `IOptions<T>` + `ValidateOnStart` (mirrors `OpenAIOptions`), sourced from environment/secret manager, never logged, never sent to the client. Browser gets only a scoped, time-limited STT token. |
| §14 Observability | PASS | New `VoiceProviderFailoverEvent` log + admin-visible signal (FR-039) mirrors the existing `ProviderHealthCheck`/admin health UI pattern; failover events logged via Serilog with correlation id, no secrets/PII beyond user id. |
| §5 Database, audit, soft delete | PASS | New entities use `BaseEntity`/audit interceptor; `VoiceProviderFailoverEvent` is an append-only operational log with no soft delete, mirroring `ProviderHealthCheck`'s documented exception. |
| §10 Testing | PASS (planned in tasks) | New Application handlers unit-tested with faked provider interfaces; new Infrastructure adapters integration-tested against recorded/replayed HTTP fixtures; new frontend hooks/state machine covered by Vitest+RTL; new voice controls covered by jest-axe. |
| §3 "swap a provider via Infrastructure-only change" | PASS, with a documented nuance | STT specifically has an inherent transport asymmetry: the primary path is a **browser-direct** WebSocket to ElevenLabs (token-minted server-side), while the fallback path is the **existing backend-mediated** Whisper upload. The backend-owned parts (token minting, TTS relay, health signal) are fully interchangeable via the abstraction; *which capture pipeline the browser runs* is necessarily a frontend orchestration decision reading a health signal, not a Domain/Application concern. This is a justified consequence of FR-041 (never route raw audio through the backend when avoidable) and SC-001 (latency), not a violation — see research.md Decision 2. |

No Complexity Tracking entries are required — the one architectural nuance above is explained
and accepted, not a rule broken without justification.

**Known limitation carried into research.md (not a gate failure):** FR-025 requires the
sphere to react to "the actual audio being played back," but the *fallback* engine is the
browser's native `speechSynthesis`, which — as the existing code already documents
(`useTextToSpeech.ts`) — exposes no analyzable audio stream at all, in either engine. This is
a pre-existing constraint of the browser API being kept as the fallback, not something this
feature can close without changing the fallback engine itself (out of scope — the fallback is
explicitly the *unmodified* legacy implementation). The plan treats this as an accepted,
documented limitation of degraded/fallback mode specifically — real FFT-based reactivity
applies to the primary (ElevenLabs) path only. Flagged for visibility, not blocking.

## Project Structure

### Documentation (this feature)

```text
specs/012-elevenlabs-voice-engine/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── voice-stt-session.md
│   ├── voice-reply-stream.md
│   ├── voice-preferences.md
│   └── voice-provider-health.md
└── tasks.md             # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

Existing two-part layout (layered .NET backend + React SPA) — extended, not restructured.

```text
src/AskLucy.Domain/Ai/
├── UserVoicePreference.cs                 # NEW — mirrors UserAiPreference.cs
└── VoiceProviderFailoverEvent.cs          # NEW — mirrors the shape of ProviderHealthCheck

src/AskLucy.Application/
├── Abstractions/
│   ├── ISpeechToTextSessionProvider.cs    # NEW
│   ├── ITextToSpeechProvider.cs           # NEW
│   └── IVoiceProviderHealthRecorder.cs    # NEW
└── Ai/
    ├── Commands/
    │   ├── CreateSpeechToTextSession/     # NEW
    │   ├── StreamVoiceReply/              # NEW — orchestrates the existing LLM stream + TTS
    │   └── SaveUserVoicePreference/       # NEW — mirrors SaveUserAiPreference
    └── Queries/
        ├── GetUserVoicePreference/        # NEW — mirrors GetUserAiPreference
        └── GetVoiceProviderHealth/        # NEW — admin-only

src/AskLucy.Infrastructure/Ai/
├── ElevenLabsOptions.cs                   # NEW — mirrors OpenAIOptions.cs
├── ElevenLabsSpeechToTextSessionProvider.cs   # NEW
├── ElevenLabsTextToSpeechProvider.cs      # NEW
└── VoiceProviderHealthRecorder.cs         # NEW

src/AskLucy.Persistence/
├── Configurations/
│   ├── UserVoicePreferenceConfiguration.cs        # NEW
│   └── VoiceProviderFailoverEventConfiguration.cs # NEW
└── Migrations/
    └── <timestamp>_AddVoiceEngineTables.cs        # NEW

src/AskLucy.Web/Controllers/v1/
└── AiController.cs                        # EXTENDED — new actions on the existing controller:
                                            #   POST /api/v1/ai/voice/stt-session
                                            #   POST /api/v1/ai/voice/reply
                                            #   GET/PUT /api/v1/ai/voice/preferences
                                            #   GET /api/v1/ai/voice/health  (admin)

src/AskLucy.Web/ClientApp/src/features/chat/voice/
├── useWavRecorder.ts                      # UNCHANGED — becomes the fallback STT capture path
├── useTextToSpeech.ts                     # UNCHANGED — becomes the fallback TTS path
├── voicePersonaMap.ts / selectPersonaVoice.ts  # UNCHANGED — reused by the fallback path
├── useSpeechRecognition.ts                # NEW — ElevenLabs realtime STT client (primary)
├── useSpeechSynthesis.ts                  # NEW — consumes /voice/reply's streamed audio (primary)
├── useConversationAudio.ts                # NEW — Conversation Coordinator (one Voice Turn)
├── useVoiceAnalyzer.ts                    # NEW — single shared AudioContext/AnalyserNode
├── useVoiceState.ts                       # NEW — centralized Voice State Machine (Zustand)
├── voiceProviderStatus.ts                 # NEW — primary/fallback health + auto-recovery retry
└── voicePreferencesStore.ts               # NEW — Zustand `persist` store, mirrors themeStore.ts

src/AskLucy.Web/ClientApp/src/features/chat/components/
└── VoiceControlBar.tsx                    # NEW — mic/mute/stop/mode-toggle UI (FR-020/FR-024)

src/AskLucy.Web/ClientApp/src/features/chat/scene/
└── ReactiveSphere.tsx                     # UNCHANGED signature — its `getReactiveIntensity`
                                            # getter is now supplied by useVoiceAnalyzer instead
                                            # of useTextToSpeech's timing approximation (primary
                                            # path only; fallback keeps the existing approximation)
```

**Structure Decision**: Extends the existing layered backend (`Domain` → `Application` →
`Infrastructure`/`Persistence` → `Web`) and the existing `src/features/chat/voice/` frontend
module. No new project, no new top-level directory, no restructuring of the existing sphere
or chat code — consistent with constitution §7's "Convention Over Configuration."

## Post-Design Re-check

Re-evaluated after Phase 1 (data-model.md, contracts/, quickstart.md): no new violations were
introduced by the concrete data model or contracts. The `/api/v1/ai/voice/reply` multiplexed
event stream (contracts/voice-reply-stream.md) is a structured evolution of the existing
`/api/v1/ai/chat` SSE convention (adds a `type` discriminator per event instead of raw text
deltas) to carry audio alongside text — not a new architectural pattern requiring an ADR, since
it stays within "Server-Sent Events (or an equivalent chunked-transfer mechanism)" (§6). All
gates above remain PASS.

## Complexity Tracking

*No entries — Constitution Check has no unjustified violations.*
