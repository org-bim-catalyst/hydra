# ADR-0006: ElevenLabs Voice Engine — standalone STT/TTS, not Speech Engine; browser-direct STT via a minted token

**Status**: Accepted
**Date**: 2026-08-02
**Deciders**: Engineering (via `/speckit-plan`/`/speckit-analyze` for SPEC-012)

## Context

`specs/012-elevenlabs-voice-engine` replaces the existing Whisper.net (STT) and browser
`speechSynthesis` (TTS) implementation with ElevenLabs, building a production conversational
voice system (Push-to-Talk and Continuous modes, natural interruption) comparable to ChatGPT
Advanced Voice Mode — with the existing implementation kept as a permanent automatic fallback
if ElevenLabs is unreachable (spec.md Clarifications). Two decisions here are the kind
constitution §17 requires an ADR for: a new architectural pattern with no prior use in this
codebase, and a choice — which vendor surface to build against — that is expensive to reverse
once the conversation orchestration and client wiring are built on top of it.

## Decision 1: Integrate ElevenLabs' standalone STT/TTS APIs, not its "Speech Engine" conversational-AI product

ElevenLabs offers two different ways to add voice: standalone Speech-to-Text ("Scribe") and
Text-to-Speech APIs used as independent I/O capabilities, or "Speech Engine" — a
self-contained conversational-AI product that owns the LLM turn itself over one WebSocket
(ElevenLabs transcribes the user's speech, forwards the transcript to the caller's own
server, and that server's reply flows back out through the same session).

**Decision**: Use only the standalone STT and TTS APIs, as speech I/O either side of Ask
Lucy's *already-existing* LLM turn. STT converts speech to text that flows into the existing
`SendChatMessageCommand`/`IAIProviderResolver` pipeline exactly as typed chat does today; TTS
converts that pipeline's streamed text output into speech. Speech Engine is rejected outright.

**Rationale**: Adopting Speech Engine would make a third-party vendor's WebSocket session the
conversation orchestrator, which conflicts with:
- Constitution §9 (`ILlmProvider`-style abstraction — no Application/Domain code references a
  specific vendor SDK): Speech Engine's LLM-calling step would run *inside* ElevenLabs' own
  session, invisible to and unabstracted from Ask Lucy's multi-provider AI engine
  (specs/005-multi-provider-ai-engine).
- The existing chat persistence pipeline: Speech Engine's own turn loop has no path back into
  `AppendMessageCommand`/`UserChat`/RAG/memory without building a parallel, duplicate
  conversation pipeline.
- The feature's own "reused conversation/session infrastructure" assumption — a competing
  orchestrator is the opposite of reuse, and would make the "fall back to the legacy
  implementation on failure" requirement far harder to satisfy uniformly.

**Alternatives considered**:
- *ElevenLabs Speech Engine end-to-end* — rejected per Rationale above.
- *A hybrid using Speech Engine only for STT+turn-detection, discarding its LLM call and
  keeping only the transcript* — rejected as needless complexity and cost: Speech Engine's
  pricing and session model charges for the full orchestrated session regardless, and the
  standalone Scribe realtime STT API already provides exactly the transcript-only capability
  needed, more simply and more cheaply.

## Decision 2: Browser connects directly to ElevenLabs for STT; the backend only mints a short-lived token

**Decision**: The backend exposes `POST /api/v1/ai/voice/stt-session`, which calls ElevenLabs
server-side (using the platform's secret API key) to obtain a single-use token scoped to one
session and returns only that token to the browser. The browser then opens a WebSocket
**directly to ElevenLabs** using that token (`useSpeechRecognition.ts`) — raw microphone audio
never transits Ask Lucy's own backend for the primary STT path. This is a new pattern in this
codebase: every other AI provider integration (OpenAI/Anthropic/Gemini/OpenRouter) is called
exclusively server-side, with the browser only ever seeing our own API.

**Rationale**:
- **Security** (constitution §8): the real ElevenLabs API key never leaves the server; the
  browser only ever holds a token that expires quickly and is scoped to one session. This
  mirrors the precedent already established by `SignedUrlService` (HMAC/time-limited,
  Data-Protection-backed credentials for direct client access without exposing the underlying
  secret) — not reused code, but a precedented pattern applied to a new transport.
- **Latency**: proxying realtime audio frames through the backend before they reach ElevenLabs
  would add a full extra network hop to every chunk, directly working against the low-latency
  partial-transcript requirement (FR-001, SC-001/SC-003).
- **Operational simplicity**: relaying a live WebSocket through the backend would require the
  host to support long-lived bidirectional WebSocket proxying for this one feature — a new
  class of infrastructure requirement this ADR avoids taking on.

**Alternatives considered**:
- *Proxy all STT audio through the backend, backend relays to ElevenLabs* — rejected: adds
  the latency and inbound-WebSocket-hosting cost above for no security benefit over a
  short-lived, single-use token.
- *Keep audio capture WAV-batch (as today) and swap Whisper for a batch ElevenLabs STT call*
  — rejected: satisfies neither FR-001's streaming requirement nor the SC-001/SC-003 latency
  targets, which need partial transcripts and continuous VAD-driven turn detection, not
  end-of-recording batch transcription.

**Fallback path is unaffected**: Whisper.net stays exactly as it is today — the browser
records a WAV via `useWavRecorder.ts` and posts it to the existing
`/api/v1/ai/transcriptions/microphone` endpoint. No transport change on the fallback side, and
no change to the never-a-silent-outage-source in `IUserVoicePreferenceRepository`'s existing
credential handling.

See `specs/012-elevenlabs-voice-engine/research.md` Decisions 1–2 for the full alternatives
analysis this ADR summarizes.

## Consequences

- Ask Lucy's backend now brokers short-lived third-party tokens for direct client-to-vendor
  connections — a pattern future browser-direct integrations (e.g., a future streaming
  transcription or realtime vision vendor) can reuse, rather than defaulting to a full backend
  proxy each time.
- TTS still flows entirely server-side (`ElevenLabsTextToSpeechProvider`, relayed over the
  existing SSE-style chunked-streaming convention) — only STT is browser-direct. The two
  capabilities are wired independently, so a future switch of either vendor in isolation (per
  the multi-provider abstraction goal) does not require re-deciding the other's transport.
- If ElevenLabs' realtime STT wire protocol (message names/fields) changes, only
  `useSpeechRecognition.ts` and `ElevenLabsSpeechToTextSessionProvider` need updating — no
  Application/Domain code references ElevenLabs directly, consistent with constitution §9.
