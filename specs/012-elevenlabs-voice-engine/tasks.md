---

description: "Task list for ElevenLabs Conversational Voice Engine"
---

# Tasks: ElevenLabs Conversational Voice Engine

**Input**: Design documents from `/specs/012-elevenlabs-voice-engine/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md
(all present)

**Tests**: Included — this project's constitution (§10 Testing Standards, §16/§19 Quality
Gates/Definition of Done) requires tests for new/changed behavior, not an optional add-on.
Full end-to-end audio automation (real microphone/speaker hardware in CI) is explicitly out
of scope per quickstart.md's own note; automated tests instead cover contracts, handlers, and
hooks with mocked WebSocket/fetch/Web Audio boundaries, and quickstart.md's 6 scenarios are
the manual/real-hardware validation pass (Phase 7).

**Organization**: Tasks are grouped by user story (spec.md priorities P1–P4) so each story is
independently implementable, testable, and shippable.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1/US2/US3/US4, mapping to spec.md's four user stories
- File paths are exact and repository-relative

## Path Conventions

Existing layered web app, extended (no new project) — see plan.md "Project Structure":
- Backend: `src/AskLucy.{Domain,Application,Infrastructure,Persistence,Web}/`
- Frontend: `src/AskLucy.Web/ClientApp/src/features/chat/`
- Tests: `tests/AskLucy.{Domain,Application,Infrastructure,Web}.Tests/` (backend),
  co-located `*.test.ts(x)` (frontend, existing convention)

---

## Phase 1: Setup

**Purpose**: ElevenLabs configuration scaffolding — no new packages needed (plan.md).

- [X] T001 [P] Add `ElevenLabs` configuration section (API key sourced from environment/user-
      secrets only — never a literal value) to `src/AskLucy.Web/appsettings.json` and
      `src/AskLucy.Web/appsettings.Development.json`, per constitution §8
- [X] T002 [P] Create `ElevenLabsOptions` (`ApiKey` required; `VoiceId`, `ModelId` default
      `"eleven_v3"`, default `Stability`/`SimilarityBoost`/`Style`/`Speed`/
      `UseSpeakerBoost`, `OutputFormat` default `"mp3_44100_128"`, `BaseUrl` default
      `"https://api.elevenlabs.io/v1/"` — mirrors `OpenAIOptions.cs`) in
      `src/AskLucy.Infrastructure/Ai/ElevenLabsOptions.cs`
- [X] T003 Register `ElevenLabsOptions` via `AddOptions<ElevenLabsOptions>().Bind(...)
      .ValidateOnStart()` and a named `HttpClient("ElevenLabs")` (BaseAddress from options)
      in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T002)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The speech-provider abstraction, persistence, orchestration endpoints, and
shared frontend plumbing every user story depends on — including the FR-033–FR-037
fallback/recovery mechanism, which has no dedicated user story of its own but is a MUST from
the very first voice turn.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Domain

- [X] T004 [P] Create `UserVoicePreference` entity (data-model.md fields; lazy-create pattern
      like `UserAiPreference`) in `src/AskLucy.Domain/Ai/UserVoicePreference.cs`
- [X] T005 [P] Create `VoiceProviderFailoverEvent` entity (append-only, no soft delete;
      `Direction` enum `FailedOverToFallback`/`RecoveredToPrimary`) in
      `src/AskLucy.Domain/Ai/VoiceProviderFailoverEvent.cs`

### Application abstractions

- [X] T006 [P] Create `ISpeechToTextSessionProvider` (`Task<SpeechToTextSession>
      CreateSessionAsync(string language, CancellationToken)`, returning a token + expiry —
      `language` per research.md Decision 9) in
      `src/AskLucy.Application/Abstractions/ISpeechToTextSessionProvider.cs`
- [X] T007 [P] Create `ITextToSpeechProvider` (`IAsyncEnumerable<byte[]> StreamSpeechAsync
      (string textChunk, VoiceSettingsDto settings, CancellationToken)`) in
      `src/AskLucy.Application/Abstractions/ITextToSpeechProvider.cs`
- [X] T008 [P] Create `IVoiceProviderHealthRecorder` (`RecordFailoverAsync(userId, reason,
      ct)`, `RecordRecoveryAsync(userId, ct)`) in
      `src/AskLucy.Application/Abstractions/IVoiceProviderHealthRecorder.cs`
- [X] T009 [P] Create `IUserVoicePreferenceRepository` and
      `IVoiceProviderFailoverEventRepository` in
      `src/AskLucy.Application/Abstractions/IUserVoicePreferenceRepository.cs` and
      `IVoiceProviderFailoverEventRepository.cs`
- [X] T010 [P] Create `VoiceSettingsDto` (voiceId, stability, similarityBoost, style, speed,
      useSpeakerBoost) in `src/AskLucy.Application/Ai/VoiceSettingsDto.cs`

### Infrastructure & Persistence

- [X] T011 Implement `ElevenLabsSpeechToTextSessionProvider` (POSTs to ElevenLabs' token-mint
      endpoint via the named `HttpClient`, passing `language` as ElevenLabs' `language_code`
      per research.md Decision 9; maps failures to the existing
      `AiProviderUnavailableException`/`AiProviderRateLimitedException` types from spec 005)
      in `src/AskLucy.Infrastructure/Ai/ElevenLabsSpeechToTextSessionProvider.cs` (depends on
      T003, T006)
- [X] T012 Implement `ElevenLabsTextToSpeechProvider` (POSTs to ElevenLabs' streaming TTS
      endpoint via the named `HttpClient`, yields audio byte chunks as they arrive, same
      exception-mapping convention as T011) in
      `src/AskLucy.Infrastructure/Ai/ElevenLabsTextToSpeechProvider.cs` (depends on T003, T007)
- [X] T013 [P] Extend `ElevenLabsOptions` with a per-language voice-id map (research.md
      Decision 9, mirroring the existing `voicePersonaMap.ts` structure used by the fallback
      engine) in `src/AskLucy.Infrastructure/Ai/ElevenLabsOptions.cs` (depends on T002)
- [X] T014 Implement `VoiceProviderHealthRecorder` (writes `VoiceProviderFailoverEvent` rows;
      logs via Serilog with correlation id; never logs raw provider exception text that could
      contain the API key, per constitution §14) in
      `src/AskLucy.Infrastructure/Ai/VoiceProviderHealthRecorder.cs` (depends on T005, T008)
- [X] T015 [P] Create `UserVoicePreferenceConfiguration` (unique index on `UserId`) in
      `src/AskLucy.Persistence/Configurations/UserVoicePreferenceConfiguration.cs`
- [X] T016 [P] Create `VoiceProviderFailoverEventConfiguration` (index `UserId` +
      `OccurredAtUtc`, no soft-delete query filter) in
      `src/AskLucy.Persistence/Configurations/VoiceProviderFailoverEventConfiguration.cs`
- [X] T017 Add `DbSet<UserVoicePreference>` and `DbSet<VoiceProviderFailoverEvent>` in
      `src/AskLucy.Persistence/AskLucyDbContext.cs` (depends on T004, T005)
- [X] T018 [P] Implement `UserVoicePreferenceRepository` and
      `VoiceProviderFailoverEventRepository` in
      `src/AskLucy.Persistence/Repositories/UserVoicePreferenceRepository.cs` and
      `VoiceProviderFailoverEventRepository.cs` (depends on T009)
- [X] T019 Register the two new repositories (`src/AskLucy.Persistence/
      DependencyInjection.cs`) and `ElevenLabsSpeechToTextSessionProvider`/
      `ElevenLabsTextToSpeechProvider`/`VoiceProviderHealthRecorder` (`src/
      AskLucy.Infrastructure/DependencyInjection.cs`) (depends on T011–T014, T018)
- [X] T020 Generate EF Core migration `AddVoiceEngineTables` (`dotnet ef migrations add`) in
      `src/AskLucy.Persistence/Migrations/` (depends on T015–T017)

### Backend orchestration endpoints (shared by every user story)

- [X] T021 [P] Create `CreateSpeechToTextSessionCommand`/Handler (accepts `language`; calls
      `ISpeechToTextSessionProvider`; on failure calls
      `IVoiceProviderHealthRecorder.RecordFailoverAsync` and rethrows the existing
      provider-unavailable exception type; on success after a prior failover, calls
      `RecordRecoveryAsync`) in `src/AskLucy.Application/Ai/Commands/
      CreateSpeechToTextSession/` (depends on T006, T008)
- [X] T022 Create `StreamVoiceReplyCommand`/Handler (accepts `language`; reuses
      `SendChatMessageCommand`'s internal LLM streaming call via `IAIProviderResolver`;
      buffers text deltas into sentence-sized chunks; feeds each chunk to
      `ITextToSpeechProvider.StreamSpeechAsync`, resolving voice settings from
      `IUserVoicePreferenceRepository` falling back to `ElevenLabsOptions`' per-language
      default then its platform-wide default (research.md Decision 9); yields a merged
      transcript-delta/audio-chunk/usage event sequence; on a TTS-specific failure
      mid-stream, calls `IVoiceProviderHealthRecorder.RecordFailoverAsync` and yields an
      `audio-failed` event without terminating the text stream — contracts/
      voice-reply-stream.md) in `src/AskLucy.Application/Ai/Commands/StreamVoiceReply/`
      (depends on T007, T008, T009, T010, T013)
- [X] T023 Add `POST /api/v1/ai/voice/stt-session` and `POST /api/v1/ai/voice/reply` actions
      to `AiController` (contracts/voice-stt-session.md, contracts/voice-reply-stream.md;
      same `[Authorize]` + `[EnableRateLimiting("ai-endpoints")]` as existing actions;
      `/voice/reply` persists the user + assistant `Message` via the existing
      `AppendMessageCommand` composition exactly like `Chat()` does, and writes the
      multiplexed `data: {...}\n\n` JSON-enveloped stream) in
      `src/AskLucy.Web/Controllers/v1/AiController.cs` (depends on T021, T022)
- [X] T024 [P] Create `SaveUserVoicePreferenceCommand`/Handler/Validator (validates
      `voiceSpeed`/`voiceStyle` ranges, rejects out-of-range values with a specific message)
      in `src/AskLucy.Application/Ai/Commands/SaveUserVoicePreference/` (depends on T009)
- [X] T025 [P] Create `GetUserVoicePreferenceQuery`/Handler (returns platform defaults if no
      row exists yet) in `src/AskLucy.Application/Ai/Queries/GetUserVoicePreference/`
      (depends on T009)
- [X] T026 Add `GET`/`PUT /api/v1/ai/voice/preferences` actions to `AiController`
      (contracts/voice-preferences.md) in `src/AskLucy.Web/Controllers/v1/AiController.cs`
      (depends on T024, T025)

### Frontend shared plumbing

- [X] T027 [P] Create `voiceApi.ts` (`createSttSession(language)`, `streamVoiceReply()`
      parsing the multiplexed event stream via a `ReadableStream` reader — same pattern as
      `aiApi.ts`'s `streamChat` — `getVoicePreferences()`, `saveVoicePreferences()`) in
      `src/AskLucy.Web/ClientApp/src/features/chat/api/voiceApi.ts`
- [X] T028 [P] Create `useVoiceState.ts` (centralized Voice State Machine — Idle/Listening/
      UserSpeaking/Processing/AiThinking/AiSpeaking/Interrupted/Muted/Error — Zustand store,
      no persistence) in `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceState.ts`
- [X] T029 [P] Create `useVoiceAnalyzer.ts` (owns one shared `AudioContext`+`AnalyserNode`
      per voice session; decodes/plays incoming `audio-chunk` bytes through it; exposes a
      ref-based `getReactiveIntensity(): number` computed from `getByteFrequencyData`,
      matching `useTextToSpeech.getIntensity()`'s existing signature — research.md Decision
      6) in `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceAnalyzer.ts`
- [X] T030 Create `voiceProviderStatus.ts` (tracks primary/fallback per session; on
      `createSttSession()`/`streamVoiceReply()` failure, flips to fallback and surfaces the
      "reduced quality" notice; before each new turn while on fallback, retries
      `createSttSession()` as a health probe and flips back to primary on success —
      research.md Decision 5) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/voiceProviderStatus.ts` (depends on
      T027)
- [X] T031 [P] Create `voicePreferencesStore.ts` (Zustand `persist`/localStorage cache of the
      last-synced preferences, mirroring `themeStore.ts`, kept in sync with `voiceApi`'s
      get/save calls) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/voicePreferencesStore.ts` (depends
      on T027)

### Cross-cutting tests

- [X] T032 [P] Unit tests for `UserVoicePreference`/`VoiceProviderFailoverEvent` domain
      validation in `tests/AskLucy.Domain.Tests/Ai/UserVoicePreferenceTests.cs` and
      `VoiceProviderFailoverEventTests.cs`
- [X] T033 [P] Integration tests for `ElevenLabsSpeechToTextSessionProvider`/
      `ElevenLabsTextToSpeechProvider` against recorded/replayed HTTP fixtures (constitution
      §10 — no live ElevenLabs calls in CI) in
      `tests/AskLucy.Infrastructure.Tests/Ai/ElevenLabsSpeechToTextSessionProviderTests.cs`
      and `ElevenLabsTextToSpeechProviderTests.cs`
- [X] T034 [P] Unit tests for `StreamVoiceReplyCommandHandler` (faked
      `ITextToSpeechProvider`/`IAIProviderResolver`): sentence-chunk buffering, and a
      TTS-failure-mid-stream still completes the text stream and emits `audio-failed` in
      `tests/AskLucy.Application.Tests/Ai/StreamVoiceReplyCommandHandlerTests.cs`

**Checkpoint**: Foundation ready — voice provider abstraction, persistence, orchestration
endpoints, fallback mechanism, and shared frontend plumbing all exist; no end-user voice UI
is wired up yet. User story implementation can now begin.

---

## Phase 3: User Story 1 - Push-to-Talk voice exchange with natural speech (Priority: P1) 🎯 MVP

**Goal**: A user clicks the mic, speaks, and hears a natural ElevenLabs voice reply that
starts playing before the full response finishes generating, with the sphere reacting to
real audio.

**Independent Test**: Click the mic, speak a sentence, confirm a transcript appears, confirm
audio audibly starts within ~2s of the first text delta (not after `done`), confirm the mic
returns to idle after playback (spec.md User Story 1).

### Tests for User Story 1

- [X] T035 [P] [US1] Contract test for `POST /api/v1/ai/voice/stt-session`
      (contracts/voice-stt-session.md) in
      `tests/AskLucy.Web.Tests/Controllers/AiControllerVoiceTests.cs`
- [X] T036 [P] [US1] Contract test for `POST /api/v1/ai/voice/reply` event ordering
      (transcript-delta/audio-chunk/usage/done, contracts/voice-reply-stream.md) in
      `tests/AskLucy.Web.Tests/Controllers/AiControllerVoiceTests.cs`
- [X] T037 [P] [US1] Vitest test for `useSpeechRecognition` (mocked `WebSocket`: partial/
      committed transcript events, commit-on-silence) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.test.ts`
- [X] T038 [P] [US1] Vitest test for `useSpeechSynthesis` (mocked `voiceApi.streamVoiceReply`:
      playback starts on the first `audio-chunk`, not on `done`) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechSynthesis.test.ts`
- [X] T039 [P] [US1] Vitest test for `useSpeechRecognition`'s reconnect-then-failover
      boundary (research.md Decision 8): a single dropped WebSocket connection reconnects
      transparently within the 2-retry/1s-apart budget without triggering
      `voiceProviderStatus`'s failover (FR-004); exhausting the retry budget does trigger it
      (FR-033) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.test.ts`
- [X] T040 [P] [US1] Vitest test: primary STT failure combined with a fallback capture
      failure (e.g., microphone permission denied) surfaces a visible error with retry/exit
      options, never an indefinite listening/processing state (FR-032/FR-036) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.test.ts`

### Implementation for User Story 1

- [X] T041 [US1] Create `useSpeechRecognition.ts` (calls `voiceApi.createSttSession(language)`,
      opens a `WebSocket` **directly to ElevenLabs** using the returned token per research.md
      Decision 2, streams 16kHz PCM chunks reusing `useWavRecorder.ts`'s existing
      `AudioWorkletNode` capture/downsampling internals, emits partial/final transcript
      events, calls `commit()` on detected silence — FR-001/FR-002) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts` (depends on
      T027, T030)
- [X] T042 [US1] Add bounded reconnect-with-backoff to `useSpeechRecognition.ts` (research.md
      Decision 8: up to 2 retries, 1 second apart, before calling `voiceProviderStatus`'s
      failover — FR-004/FR-033 boundary) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts` (depends on
      T041, T030)
- [X] T043 [US1] Create `useSpeechSynthesis.ts` (calls `voiceApi.streamVoiceReply()`, routes
      `audio-chunk` events into `useVoiceAnalyzer` for playback + visualization, surfaces
      `transcript-delta` events for on-screen text — FR-008) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechSynthesis.ts` (depends on
      T027, T029, T030)
- [X] T044 [US1] Create `useConversationAudio.ts` (Conversation Coordinator for one Voice
      Turn: mic click → `useSpeechRecognition` → finalized transcript → `useSpeechSynthesis`
      → playback complete → mic returns to idle; drives `useVoiceState` transitions
      Idle→Listening→Processing→AiSpeaking→Idle) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T028, T041, T043)
- [X] T045 [US1] Add a microphone button to `ChatComposer.tsx` (Push-to-Talk: click to
      start/stop, wired to `useConversationAudio` — FR-013) in
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx` (depends on
      T044)
- [X] T046 [US1] Feed `ReactiveSphere.tsx`'s existing `getReactiveIntensity` prop (currently
      supplied from `useTextToSpeech()` in `ChatPage.tsx`) from `useVoiceAnalyzer` during an
      active primary-path voice turn, falling back to the existing
      `useTextToSpeech.getIntensity()` otherwise — FR-025–FR-028; no changes inside
      `ReactiveSphere.tsx` itself — in
      `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on T029)
- [X] T047 [US1] Pass `ChatPage.tsx`'s existing `language` state into
      `useSpeechRecognition`/`useSpeechSynthesis` calls (research.md Decision 9, FR-007/
      FR-012) in `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on
      T041, T043)
- [X] T048 [US1] Add microphone visual states — idle/listening/processing/disabled/
      permission-required (FR-020, partial; user-speaking/AI-speaking complete in US2/US3) —
      driven by `useVoiceState`, to the mic button in
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx` (depends on
      T045)
- [X] T049 [US1] Handle microphone permission request/denial with a distinct visible state
      (FR-003) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts`
- [X] T050 [US1] Implement the dual-failure error path in `useConversationAudio.ts`: when
      `voiceProviderStatus` reports the primary has failed **and** the fallback
      capture/synthesis also errors, transition `useVoiceState` to `Error` with retry/exit-
      voice-mode actions surfaced (FR-032/FR-036) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T030, T044, T049)

**Checkpoint**: User Story 1 fully functional and independently testable — Push-to-Talk voice
conversation with streaming ElevenLabs STT/TTS, language parity, bounded reconnect, dual-
failure error handling, and real sphere reactivity. **This is the feature's MVP.**

---

## Phase 4: User Story 2 - Hands-free Continuous Conversation Mode (Priority: P2)

**Goal**: A user talks back-and-forth with the AI with no clicks between turns.

**Independent Test**: Toggle Continuous mode, speak without clicking anything, let the AI
respond, confirm listening resumes automatically within ~1s across at least two full turns
(spec.md User Story 2).

### Tests for User Story 2

- [X] T051 [P] [US2] Vitest test for VAD-driven automatic speech-start/stop detection in
      `useSpeechRecognition` under Continuous mode in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.test.ts`
- [X] T052 [P] [US2] Vitest test for `useConversationAudio`'s auto-relisten loop (AI finishes
      speaking → listening resumes with zero manual action) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.test.ts`

### Implementation for User Story 2

- [X] T053 [US2] Add Continuous Conversation Mode support to `useSpeechRecognition.ts` (keep
      the WebSocket/mic open across turns; rely on ElevenLabs' VAD/endpoint-detection events
      for automatic speech-start/commit instead of a manual stop — FR-014) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts` (depends on
      T041)
- [X] T054 [US2] Extend `useConversationAudio.ts`'s turn loop to auto-resume listening
      immediately after `useSpeechSynthesis` reports playback complete, when in Continuous
      mode (FR-014) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T044, T053)
- [X] T055 [US2] Add a Conversation Mode toggle (Push-to-Talk/Continuous) to
      `ChatComposer.tsx`, wired to `useConversationAudio`'s mode without tearing down/
      recreating the audio pipeline (FR-015) in
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx` (depends on
      T045)
- [X] T056 [US2] Persist `conversationMode` via `voicePreferencesStore`/
      `voiceApi.saveVoicePreferences()` on toggle, and restore it on mount (FR-016) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/voicePreferencesStore.ts` (depends
      on T031, T055)
- [X] T057 [US2] Add "user speaking"/"AI thinking" mic visual states, completing FR-020's
      state set for the hands-free loop, in
      `src/AskLucy.Web/ClientApp/src/features/chat/components/ChatComposer.tsx` (depends on
      T048)

**Checkpoint**: User Stories 1 and 2 both independently functional — the full hands-free
conversation loop works end-to-end.

---

## Phase 5: User Story 3 - Natural interruption of AI speech (Priority: P3)

**Goal**: The user can start talking over the AI and it stops immediately and listens.

**Independent Test**: While the AI is speaking, start talking; confirm playback stops
immediately (well under 300ms in repeated trials), no further audio from that reply plays,
and the system responds to the new message, not a continuation (spec.md User Story 3).

### Tests for User Story 3

- [X] T058 [P] [US3] Vitest test: speech detected while `useVoiceState` is `AiSpeaking`
      triggers immediate playback cancellation and a transition to `Interrupted` →
      `Listening` in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.test.ts`
- [X] T059 [P] [US3] Vitest test for the local fast-path pre-trigger (research.md Decision
      10): a brief non-speech noise ducks then resumes playback (false-positive path); real
      speech ducks then fully cancels once the authoritative transcript event confirms it
      (confirmed path) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.test.ts`

### Implementation for User Story 3

- [X] T060 [US3] Add a local amplitude-threshold pre-trigger to `useSpeechRecognition.ts`
      that ducks AI playback via `useVoiceAnalyzer`'s gain node immediately on detected
      speech-like audio (reusing `useWavRecorder.ts`'s peak-level technique), ahead of
      ElevenLabs' authoritative VAD confirmation (research.md Decision 10) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts` (depends on
      T029, T041)
- [X] T061 [US3] In `useSpeechRecognition.ts`, keep listening for speech-start even while
      `useVoiceState` is `AiSpeaking` — don't gate mic input on the AI-speaking state
      (FR-017) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useSpeechRecognition.ts` (depends on
      T041, T053)
- [X] T062 [US3] In `useConversationAudio.ts`, on the local pre-trigger (T060) firing during
      `AiSpeaking`: immediately duck playback; if ElevenLabs' authoritative transcript event
      then confirms real speech, abort `useSpeechSynthesis`'s in-flight request (cancels
      `/api/v1/ai/voice/reply` via `AbortController`, stopping both LLM generation and TTS
      together per contracts/voice-reply-stream.md's cancellation section), clear any queued
      audio in `useVoiceAnalyzer`, transition `useVoiceState` to `Interrupted` then
      immediately `Listening`, and begin capturing the new utterance; if no confirmation
      arrives (false positive), resume playback from the duck point instead (FR-017/FR-018,
      research.md Decision 10) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T029, T043, T044, T060)
- [X] T063 [US3] Ensure an interrupted reply's partial content is never resumed or appended
      to once the user's new message is processed (FR-019) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T062)

**Checkpoint**: User Stories 1–3 all independently functional — natural barge-in works in
both interaction modes, with sub-300ms perceived latency via the local duck pre-trigger.

---

## Phase 6: User Story 4 - Voice controls: mute, stop, and mode switching (Priority: P4)

**Goal**: Dedicated, keyboard-accessible controls for muting output, stopping a reply outright,
switching modes without losing context, and full preference persistence/restoration.

**Independent Test**: Mute mid-reply and confirm generation/visualization continue with only
sound suppressed; stop a reply and confirm playback+generation both halt immediately; switch
modes mid-conversation with history intact; set a distinctive preference combination, reload,
and confirm it's restored; operate every control via keyboard alone (spec.md User Story 4).

### Tests for User Story 4

- [X] T064 [P] [US4] Vitest test: muting suppresses only speaker output while generation/
      synthesis and sphere reactivity continue in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceAnalyzer.test.ts`
- [X] T065 [P] [US4] Vitest test: stop cancels playback+generation, clears the audio queue,
      resets the sphere to idle, and resumes listening in Continuous mode in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.test.ts`
- [X] T066 [P] [US4] jest-axe accessibility test for `VoiceControlBar.tsx` (keyboard
      operability, ARIA roles, focus states — constitution §7/§10) in
      `src/AskLucy.Web/ClientApp/src/features/chat/components/VoiceControlBar.test.tsx`

### Implementation for User Story 4

- [X] T067 [P] [US4] Add a mute/unmute gain control to `useVoiceAnalyzer.ts` that suppresses
      only audible output, never the analyser feed or the underlying generation/synthesis
      stream (FR-021/FR-022) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useVoiceAnalyzer.ts` (depends on
      T029)
- [X] T068 [US4] Add a stop action to `useConversationAudio.ts` (same abort path as US3's
      interruption, clear the audio queue, reset `useVoiceAnalyzer` to idle, resume listening
      automatically if Continuous mode is active — FR-023) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T062)
- [X] T069 [US4] Create `VoiceControlBar.tsx` (mic toggle, mode toggle, mute/unmute, stop —
      all states from `useVoiceState`, all keyboard-operable per FR-024) in
      `src/AskLucy.Web/ClientApp/src/features/chat/components/VoiceControlBar.tsx` (depends
      on T028, T067, T068)
- [X] T070 [US4] Wire `VoiceControlBar.tsx` into `ChatPage.tsx`, replacing the ad hoc controls
      added directly to `ChatComposer.tsx` in US1/US2 (FR-020/FR-024) in
      `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on T069)
- [X] T071 [US4] Add keyboard shortcuts (Space = hold-to-talk, M = mute/unmute, Esc = stop,
      V = toggle mode) scoped to the chat page in
      `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` (depends on T070)
- [X] T072 [US4] Extend `voicePreferencesStore.ts`/`voiceApi` wiring to persist `isMuted`,
      `selectedVoiceId`, `voiceSpeed`, `voiceStyle`, `preferredMicrophoneDeviceId`,
      `preferredSpeakerDeviceId` (mode was already wired in US2) and restore all of them on
      mount (FR-029/FR-030) in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/voicePreferencesStore.ts` (depends
      on T031, T069)
- [X] T073 [US4] Implement FR-031's "saved device no longer available" check — compare stored
      device ids against `navigator.mediaDevices.enumerateDevices()` at session start, fall
      back to the default device with a visible notice — in
      `src/AskLucy.Web/ClientApp/src/features/chat/voice/useConversationAudio.ts` (depends on
      T072)
- [X] T074 [US4] Add a voice/speed/style picker (backed by `voicePreferencesStore`) to the
      existing settings surface, mirroring the AI-preferences settings pattern from specs/
      005-multi-provider-ai-engine, in `src/AskLucy.Web/ClientApp/src/features/settings/`
      (depends on T072)

**Checkpoint**: All four user stories independently functional — the full spec.md
acceptance-scenario surface is covered.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Admin observability, documentation, and the manual/real-hardware validation pass
quickstart.md itself calls for.

- [X] T075 [P] Create `GetVoiceProviderHealthQuery`/Handler (contracts/
      voice-provider-health.md: derived `currentStatus`, `failoverCount`/`recoveryCount`,
      `events[]`) in `src/AskLucy.Application/Ai/Queries/GetVoiceProviderHealth/`
- [X] T076 Add `GET /api/v1/ai/voice/health` (`[Authorize(Roles = "Administrator")]`) to
      `AiController` (contracts/voice-provider-health.md) in
      `src/AskLucy.Web/Controllers/v1/AiController.cs` (depends on T075)
- [X] T077 [P] Extend the existing admin AI-provider health view (specs/
      007-admin-ai-provider-ui) with a read-only voice-failover panel consuming `GET
      /api/v1/ai/voice/health`, reusing existing components rather than a new admin page
      (FR-039/SC-011) in the existing admin feature directory (depends on T076)
- [X] T078 [P] Write `docs/adr/0006-elevenlabs-voice-engine-integration.md` recording: (1)
      rejecting ElevenLabs' Speech Engine product in favor of standalone STT/TTS behind the
      existing provider-abstraction pattern, and (2) the browser-direct-to-vendor STT
      connection via a backend-minted short-lived token — a new pattern in this codebase —
      with alternatives considered and consequences (constitution §17). Numbered 0006, not
      0005 — 0005 was already taken by `defer-tts-voice-persona-fix.md` by the time this task
      ran.
- [X] T079 [P] Update the project's architecture documentation to describe the new voice
      engine module boundaries (constitution §13)
- [X] T080 [P] Evaluate a Chromium fake-media-device E2E smoke test
      (`--use-fake-device-for-media-stream`/`--use-fake-ui-for-media-stream`) for at least the
      Push-to-Talk happy path in `tests/AskLucy.E2E.Tests/`, to reduce reliance on fully
      manual validation for this critical user journey (constitution §10); if infeasible,
      record why in this task's notes. Feasible — written as
      `tests/AskLucy.E2E.Tests/VoicePushToTalk.spec.ts`, verified with `playwright test
      --list`. Not runnable in this sandbox, same as every other spec in this directory
      (no live backend/frontend), plus one voice-specific requirement: a real ElevenLabs API
      key, since the fake device only supplies synthetic audio — it still has to round-trip
      through a live STT session mint and WebSocket to produce a transcript.
- [ ] T081 **BLOCKED, not done** — Run all six quickstart.md scenarios end-to-end against a
      real ElevenLabs sandbox key, including the induced-outage fallback/recovery scenarios
      (Scenario 5) and the dual-failure edge case (Scenario 6); record results in
      quickstart.md (depends on T080). This coding environment has no live ElevenLabs
      credential, no running frontend dev server + backend host, and no browser to drive —
      none of which can be substituted with unit/integration tests, since the whole point of
      this task is validating real ElevenLabs wire behavior (the residual verification risk
      research.md already flags: the realtime STT token-mint endpoint path and message field
      names were never confirmed against live documentation). Needs a human (or an agent with
      real credentials and a live deployment) to run `npm test`/manual walkthrough per
      quickstart.md and fill in the results.
- [X] T082 [P] Security review: confirm the ElevenLabs API key never appears in any client
      bundle, network response, or log at Information level or above (constitution §8/§14)
      across every new endpoint. Reviewed via static inspection (no live ElevenLabs
      connectivity in this environment, so this is not a penetration test of a running
      instance): `ElevenLabsOptions.ApiKey` is only ever read in
      `ElevenLabsSpeechToTextSessionProvider.CreateClient`/`ElevenLabsTextToSpeechProvider`'s
      equivalent, both solely to set the `xi-api-key` request header — never interpolated into
      a URL, request body, exception message, `[LoggerMessage]` call, or any DTO returned to
      the client. It is not admin-configurable via `AdminAiProvidersController` (research.md
      Decision 4 — `IOptions<T>`-bound, not the multi-provider credential table), so it never
      transits that endpoint either. Confirmed absent from the built `ClientApp/dist` bundle
      (the only "ElevenLabs" string matches are this feature's own admin-panel UI copy, not a
      credential). One residual, pre-existing-pattern note, not a new gap introduced by this
      feature: on a non-auth ElevenLabs failure, the vendor's own HTTP error response body is
      included in the thrown exception's message (`EnsureSuccessAsync`), which — same as every
      other `IAIProvider`'s failure path in this codebase — can surface to the end user (SSE
      `error` event `detail`) and to the admin failover log (`VoiceProviderFailoverEvent
      .Reason`). This is vendor-returned text, never our own request (so it cannot contain the
      key we send), but it is unverified against real ElevenLabs error payloads; flagged for
      confirmation during T081's real-sandbox run.
- [ ] T083 **BLOCKED, not done** — Performance pass: measure SC-001/SC-002/SC-003/SC-007/
      SC-010 timing targets under normal network conditions — including SC-002's interruption
      latency with and without the local duck pre-trigger (research.md Decision 10) — and
      record actual measurements against each target. Same blocker as T081: these are
      wall-clock, real-network, real-ElevenLabs measurements (time-to-first-partial-transcript,
      time-to-first-audio-chunk, interruption duck latency) that cannot be produced from this
      static sandbox without fabricating numbers. Run alongside T081 once a live deployment
      and sandbox key are available, and record actual measured values (not targets) against
      each SC in quickstart.md or a follow-up note here.
- [X] T084 [P] Add an Assumption to spec.md explicitly deferring token/cost-based throttling
      for `/api/v1/ai/voice/*` endpoints to the platform's future Billing Engine
      specification (constitution §6), mirroring specs/005-multi-provider-ai-engine's
      existing Assumption for the same platform-wide gap

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup — BLOCKS all user stories.
- **User Stories (Phase 3–6)**: All depend on Foundational completion.
  - **US2, US3, and US4 each build directly on US1's hooks** (`useSpeechRecognition`,
    `useSpeechSynthesis`, `useConversationAudio`, `ChatComposer.tsx`'s mic button) — unlike
    spec 005's mostly-independent stories, this feature's stories are **sequential
    increments on one shared conversation loop**, not parallel-safe slices. Implement in
    priority order (P1 → P2 → P3 → P4); do not start US2 before US1's Checkpoint passes.
  - Each story remains **independently testable** per its Independent Test above even though
    later stories extend earlier ones' files.
- **Polish (Phase 7)**: Depends on all four user stories being complete.

### Within Each Phase

- Tests are written before their corresponding implementation tasks and should fail first.
- Domain/Application abstractions before Infrastructure implementations before endpoints.
- Backend endpoints for a story's needs before the frontend hooks that call them.
- Hooks before the components that consume them.
- Within US1, the local reconnect/dual-failure additions (T042, T050) depend on the base
  hooks (T041, T044) existing first, even though they implement requirements (FR-004,
  FR-032/FR-036) that are conceptually foundational — they're sequenced into US1 because
  they extend files US1 creates.
- Within US3, the local fast-path pre-trigger (T060) must exist before the interruption
  handler that consumes it (T062).

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel.
- Within Foundational, all [P]-marked tasks in the same subsection (Domain, Application
  abstractions, etc.) can run in parallel; later subsections depend on earlier ones as noted
  per-task.
- Within each user story, all [P]-marked test tasks can run in parallel with each other
  before implementation begins.
- T075, T077–T080, T082–T084 in Polish can run in parallel; T081 (the full quickstart pass)
  should run after T080 (so the E2E-feasibility decision informs how much of the pass can be
  automated) and after everything else lands.

---

## Parallel Example: Foundational Phase

```bash
# Domain entities together:
Task: "Create UserVoicePreference entity in src/AskLucy.Domain/Ai/UserVoicePreference.cs"
Task: "Create VoiceProviderFailoverEvent entity in src/AskLucy.Domain/Ai/VoiceProviderFailoverEvent.cs"

# Application abstractions together (after Domain):
Task: "Create ISpeechToTextSessionProvider in src/AskLucy.Application/Abstractions/ISpeechToTextSessionProvider.cs"
Task: "Create ITextToSpeechProvider in src/AskLucy.Application/Abstractions/ITextToSpeechProvider.cs"
Task: "Create IVoiceProviderHealthRecorder in src/AskLucy.Application/Abstractions/IVoiceProviderHealthRecorder.cs"
```

## Parallel Example: User Story 1 Tests

```bash
Task: "Contract test for POST /api/v1/ai/voice/stt-session in tests/AskLucy.Web.Tests/Controllers/AiControllerVoiceTests.cs"
Task: "Contract test for POST /api/v1/ai/voice/reply event ordering in tests/AskLucy.Web.Tests/Controllers/AiControllerVoiceTests.cs"
Task: "Vitest test for useSpeechRecognition in .../voice/useSpeechRecognition.test.ts"
Task: "Vitest test for useSpeechSynthesis in .../voice/useSpeechSynthesis.test.ts"
Task: "Vitest test for the reconnect-then-failover boundary in .../voice/useSpeechRecognition.test.ts"
Task: "Vitest test for the dual-failure error path in .../voice/useConversationAudio.test.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup.
2. Complete Phase 2: Foundational (critical — blocks everything, and already includes the
   fallback/recovery mechanism, so US1 is fully spec-compliant on its own, not a stripped-down
   demo).
3. Complete Phase 3: User Story 1 (now also covers the FR-004/FR-033 reconnect boundary,
   FR-007/FR-012 language parity, and the FR-032/FR-036 dual-failure error path).
4. **STOP and VALIDATE**: run quickstart.md Scenario 1 (and Scenario 5/6 for fallback, since
   the fallback mechanism ships with the Foundational phase, not with a later story).
5. Deploy/demo if ready — this alone is a materially better voice experience than today's
   Whisper/`speechSynthesis` baseline.

### Incremental Delivery

1. Setup + Foundational → foundation ready, including fallback.
2. Add US1 → validate (quickstart Scenarios 1, 5, 6) → deploy/demo (MVP!).
3. Add US2 → validate (Scenario 2) → deploy/demo.
4. Add US3 → validate (Scenario 3, now including the local-duck-pre-trigger latency budget)
   → deploy/demo.
5. Add US4 → validate (Scenario 4) → deploy/demo.
6. Polish (admin visibility, ADR, docs, E2E-feasibility check, full quickstart pass,
   security/performance review, Billing Engine deferral note).

Unlike a feature with fully parallel-safe user stories, **this feature's stories should be
built in priority order by the same implementer(s)**, since US2–US4 each extend US1's core
hooks rather than adding independent new files — parallelizing across developers here would
mean coordinating concurrent edits to the same few files (`useConversationAudio.ts`,
`ChatComposer.tsx`), which is more coordination overhead than it saves.

---

## Notes

- [P] tasks = different files, no dependency on an incomplete task.
- [Story] label maps each task to its user story for traceability.
- Commit after each task or logical group.
- Stop at any checkpoint to validate a story independently before continuing.
- The fallback/recovery mechanism (FR-033–FR-039) ships in Foundational, not as its own user
  story phase — it has no dedicated "User Story N" in spec.md, but every story depends on it
  being correct from the start (see quickstart.md Scenario 5/6, which validate it directly
  against the Foundational + US1 slice).
- T042, T047, T050 (US1), T060/T062 (US3), and T084 (Polish) were added following
  `/speckit-analyze` findings G1, G2, U1, G3, and C1 respectively — see spec.md/research.md
  for the underlying decisions (Decisions 8, 9, 10) these tasks implement.
