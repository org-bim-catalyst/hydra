# Phase 0 Research: ElevenLabs Conversational Voice Engine

All Technical Context unknowns are resolved below; no `NEEDS CLARIFICATION` markers remain.
Findings on ElevenLabs' actual API surface come from fetching the three cookbook pages linked
in the original feature request (`text-to-speech`, `speech-to-text`, `speech-engine`) plus
the linked realtime STT guide, on 2026-08-02; several deep-linked API reference pages had
moved/404'd at fetch time — those gaps are called out explicitly rather than guessed at (see
"Residual verification risk" at the end).

## Decision 1: Integrate ElevenLabs' standalone STT/TTS APIs, not its "Speech Engine" conversational-AI product

**Decision**: Use ElevenLabs' realtime Speech-to-Text ("Scribe") and streaming Text-to-Speech
APIs as two independent capabilities. Do **not** adopt ElevenLabs' "Speech Engine" product
(the cookbook page's actual subject), which is a distinct, self-contained conversational-AI
offering that owns the LLM turn itself over one WebSocket: *"Each WebSocket connection
represents one conversation. When the user speaks, ElevenLabs transcribes the audio and sends
the transcript to your server. Your server passes it to your LLM, then streams the response
back."* Its SDK also handles turn-taking/interruption internally.

**Rationale**: Adopting Speech Engine would mean a third-party vendor's WebSocket session
becomes the conversation orchestrator, which directly conflicts with:
- Constitution §9 (`ILlmProvider`-style abstraction; "no Application/Domain code references a
  specific vendor SDK") — Speech Engine's LLM-calling step would sit *inside* ElevenLabs'
  session, invisible to and unabstracted from our own multi-provider AI engine
  (specs/005-multi-provider-ai-engine).
- The existing Conversation Manager and chat persistence — Speech Engine's own turn loop has
  no path back into `AppendMessageCommand`/`UserChat`/RAG/memory without building a parallel,
  duplicate conversation pipeline.
- Spec Assumption "Underlying conversation/session infrastructure is reused" — a competing
  orchestrator is the opposite of reuse.

Instead, this feature treats ElevenLabs purely as a **speech I/O vendor** either side of the
*already-existing* LLM turn: STT converts the user's speech to text that flows into the
existing `SendChatMessageCommand`/`IAIProviderResolver` pipeline exactly as typed chat does
today; TTS converts that pipeline's streamed text output into speech. This keeps ElevenLabs
swappable behind the same kind of interface as every other AI vendor (constitution §9),
consistent with the spec's own "future support for additional speech providers" goal.

**Alternatives considered**:
- *ElevenLabs Speech Engine end-to-end* — rejected per Rationale above.
- *A hybrid where Speech Engine handles STT+turn-detection only, discarding its LLM call and
  using only its transcript* — rejected as needless complexity: Speech Engine's pricing and
  session model charges for the full orchestrated session regardless, and the standalone
  Scribe realtime STT API already provides exactly the transcript-only capability needed,
  more simply and more cheaply.

## Decision 2: STT transport — browser connects directly to ElevenLabs; backend only mints a short-lived token

**Decision**: The backend exposes `POST /api/v1/ai/voice/stt-session`, which calls ElevenLabs
server-side (using the platform's secret API key) to obtain a **single-use, short-lived
token** and returns only that token to the browser. Per ElevenLabs' realtime STT guide:
*"Requires a single use token - this is a temporary token that can be used to connect to the
API without exposing your API key,"* and the token *"automatically expires after 15 minutes."*
The browser then opens a WebSocket **directly to ElevenLabs** using that token — raw audio
never transits our own backend for the primary STT path.

**Rationale**:
- **Security**: the real API key never leaves the server (constitution §8); the browser only
  ever holds a token scoped to one session that expires in 15 minutes. This is the same class
  of pattern already established in this codebase by `SignedUrlService` (HMAC/time-limited,
  Data-Protection-backed credentials for direct client access to a resource without exposing
  the underlying secret) — not new code reused, but a precedented pattern being applied to a
  vendor-issued token instead of a self-issued one.
- **Privacy (FR-041)**: since raw audio never reaches our backend for the primary path, "audio
  MUST NOT be retained beyond what's needed to produce the transcript" is satisfied trivially
  for that path — there's nothing on our side to retain.
- **Latency (SC-001) and hosting risk**: relaying live audio through our own backend would add
  a hop and, more importantly, would require *inbound* WebSocket support on our own ASP.NET
  host — which the fetched myASP.NET Node.js hosting guide does not confirm one way or the
  other (see "Residual verification risk"). Connecting the browser directly to ElevenLabs
  sidesteps that open question entirely for STT.

**Alternatives considered**:
- *Backend relays the audio stream to ElevenLabs (browser → our WebSocket → ElevenLabs
  WebSocket)* — rejected: doubles bandwidth, adds latency, and requires confirming inbound
  WebSocket support on the current host, which is exactly the risk Decision 2 avoids.
- *Keep audio capture WAV-batch (like today) and just swap Whisper for a batch ElevenLabs STT
  call* — rejected: it satisfies neither FR-001 ("streaming... without requiring the entire
  utterance to finish") nor SC-001/SC-003's latency targets, which need partial transcripts
  and continuous VAD-driven turn detection, not end-of-recording batch transcription.

**Fallback path is unaffected**: Whisper.net stays exactly as it is today — the browser
records a WAV via `useWavRecorder.ts` and posts it to the existing
`/api/v1/ai/transcriptions/microphone` endpoint. No transport change on the fallback side.

## Decision 3: TTS transport — backend orchestrates LLM streaming + ElevenLabs TTS together, relayed over the existing chunked-streaming convention

**Decision**: A new endpoint, `POST /api/v1/ai/voice/reply`, replaces `/api/v1/ai/chat` for
voice-mode turns only (typed chat keeps using the unmodified `/api/v1/ai/chat`). Internally it:
1. Invokes the same `SendChatMessageCommand`/`IAIProviderResolver` streaming call
   `AiController.Chat` already uses today.
2. As text deltas arrive, buffers them into sentence/clause-sized chunks.
3. Feeds each ready chunk to ElevenLabs' streaming TTS HTTP endpoint (via `IHttpClientFactory`,
   the same hand-rolled-HTTP convention as `OpenAIProvider`/`AnthropicProvider` — no ElevenLabs
   SDK) and reads the resulting audio bytes as they arrive.
4. Relays both the text deltas and the audio chunks to the browser over one chunked-transfer
   response, using the same `data: {...}\n\n` framing `/api/v1/ai/chat` already uses today,
   extended with a `type` discriminator per event (see contracts/voice-reply-stream.md) so one
   stream can carry both payload kinds.

**Rationale**: SC-001 requires audio to start playing "within 2 seconds of the AI starting to
generate its response, without waiting for the full response" — this is only achievable if
synthesis begins on the *first* sentence as soon as it's available, not after the whole reply
is generated. That requires one orchestrator holding both the LLM stream and the TTS call
open concurrently; the backend is the natural place for this since it already holds the LLM
stream open for `/api/v1/ai/chat` today, and it keeps the ElevenLabs API key server-side.
Framing the multiplexed response as an evolution of the existing SSE convention (rather than
inventing a new protocol or a WebSocket) satisfies constitution §6 ("Server-Sent Events *or an
equivalent chunked-transfer mechanism*") with no new hosting dependency.

**Alternatives considered**:
- *Client waits for the full chat response, then calls a separate synthesize-this-text TTS
  endpoint* — rejected: directly violates SC-001/FR-008 ("begin playback... rather than waiting
  for the full response").
- *A dedicated backend↔browser WebSocket for the reply stream* — rejected: same unconfirmed
  inbound-WebSocket-hosting risk as Decision 2's rejected alternative; the chunked-HTTP
  approach achieves the same low-latency multiplexing without it.
- *ElevenLabs' own TTS WebSocket, backend acting as a relay* — considered, since ElevenLabs
  does offer WebSocket TTS variants; rejected in favor of its HTTP streaming endpoint because
  the backend already uses plain `HttpClient` streaming reads for every other provider's SSE
  consumption (OpenAI/Anthropic/Gemini), and introducing a second, WebSocket-based HTTP client
  pattern into `Infrastructure.Ai` purely for this one vendor call adds a class of complexity
  (`ClientWebSocket` lifecycle, framing) the existing convention doesn't need elsewhere.

**Fallback path is unaffected**: when the session is on the fallback engine, the browser skips
`/api/v1/ai/voice/reply` for audio entirely and instead reads the reply text from the existing
`/api/v1/ai/chat` stream, passing the finalized text to the unmodified, 100%-client-side
`useTextToSpeech.ts` (browser `speechSynthesis`) exactly as it works today.

## Decision 4: ElevenLabs configuration follows the existing `IOptions<T>`-bound-vendor-options convention, not the admin-configurable multi-provider table

**Decision**: `ElevenLabsOptions` (API key, default voice id, model id, default voice
settings — stability/similarity/style/speed/speaker-boost, output format) is bound from
configuration/environment via `IOptions<ElevenLabsOptions>` with `ValidateOnStart`, exactly
like `OpenAIOptions`/`AnthropicOptions`/`WhisperOptions` today. It is **not** added as a row
in the `AIProvider` table introduced by specs/005-multi-provider-ai-engine.

**Rationale**: The `AIProvider` table models *user-selectable chat-completion vendors* an
admin can enable/disable/credential through a UI (FR-003/FR-004 of spec 005). ElevenLabs isn't
a user-selectable chat vendor — it's a fixed piece of platform infrastructure for voice I/O,
architecturally closer to `WhisperOptions` (also a fixed, non-user-selectable transcription
backend) than to the multi-provider chat abstraction. Reusing the `AIProvider` table would
force voice-specific concepts (voice id, stability/style knobs) onto a schema designed for
chat-completion capabilities, and would misleadingly expose ElevenLabs in the admin "enable/
disable AI providers" UI built for a different purpose (spec 005/007-admin-ai-provider-ui).

**Alternatives considered**:
- *Model ElevenLabs as another `AIProvider` row* — rejected per Rationale.
- *Store the API key encrypted in the database like `AIProvider.CredentialCiphertext`* —
  rejected: that mechanism exists specifically so *administrators* can rotate a credential
  through a UI without a deployment; nothing in this spec calls for an admin-facing ElevenLabs
  credential-rotation UI, and `IOptions`-bound secrets are the existing, simpler convention for
  every other fixed infrastructure credential (constitution §III YAGNI — don't build the UI
  until a requirement asks for it).

## Decision 5: Fallback/recovery policy — health-checked, per-turn retry, admin-visible

**Decision**: Voice Provider Status (spec Key Entity) is tracked as a small in-memory/session
state on the frontend, informed by two backend-observable failure points: (a) `POST
/api/v1/ai/voice/stt-session` failing or the resulting WebSocket failing to connect, and (b)
`POST /api/v1/ai/voice/reply` failing or erroring mid-stream. Either failure immediately
switches the *current* session to the fallback engine for that turn and records a
`VoiceProviderFailoverEvent` (Direction = `FailedOverToFallback`). Before each subsequent
voice turn while on fallback, the client retries `stt-session` (a cheap, fast call) as a
health probe; success flips the session back to primary and records a
`VoiceProviderFailoverEvent` (Direction = `RecoveredToPrimary`) (FR-034/SC-010). Administrators
can view aggregated failover events (FR-039/SC-011) via a read-only extension of the existing
provider-health admin view from specs/005/007-admin-ai-provider-ui, not a new admin surface.

**Rationale**: This is exactly the "fallback provider/model policy for outage or rate-limit
conditions, applied via the provider abstraction, not ad hoc try/catch per call site"
constitution §9 requires — the policy lives at the orchestration boundary (frontend voice
module + a small backend event log), not scattered across each call site. Retrying before
each new turn (rather than a background poll) is the simplest policy that satisfies "switch
back... before the user's next voice turn" (Clarifications) without adding a new background
job/hosted service purely for this.

**Alternatives considered**:
- *A backend hosted service periodically health-checking ElevenLabs*, mirroring
  `ProviderHealthCheckHostedService` — rejected for v1: that service checks *chat* providers
  on a timer because a stale "enabled" flag would otherwise mislead every user's model picker;
  voice failover instead needs to react within one active session, which a per-turn client
  probe already does more directly and more simply. Not precluded as a future addition if
  usage data shows a need — flagged, not built now (YAGNI).

## Decision 6: Real audio-reactive visualization is a data-source swap only, primary path only

**Decision**: A new `useVoiceAnalyzer` hook owns one shared `AudioContext` + `AnalyserNode` per
active voice session. For the primary (ElevenLabs) path, incoming audio chunks are decoded and
played through this analyser (feeding both the speaker output and the sphere from the same
node — no duplicate decoding/FFT, per the spec's Audio Architecture goal). `ReactiveSphere.tsx`
keeps its exact existing `getReactiveIntensity: () => number` prop contract unchanged (FR-028)
— only what supplies that getter changes, computed from `AnalyserNode.getByteFrequencyData`
instead of `useTextToSpeech`'s timing-based decay approximation.

**Rationale**: FR-025–FR-028 require real audio-derived reactivity without touching the
sphere's rendering. A single ref-getter callback is the entire integration surface already
established by the existing `useTextToSpeech.getIntensity()`/`ReactiveSphere` contract — the
new hook only needs to implement that same signature with a real signal source, which is the
minimal-surface-area way to satisfy "MUST NOT alter the sphere's existing visual design,
rendering technique, or idle animation."

**Documented limitation — fallback path**: browser `speechSynthesis` (the fallback engine)
exposes no analyzable audio stream at all; the existing code already documents this
(`useTextToSpeech.ts`: *"`window.speechSynthesis` doesn't expose its audio as an analyzable
stream"*). FR-025's "actual audio, not simulated" therefore cannot be satisfied while a
session is running in fallback mode without changing the fallback engine itself — which is
explicitly out of scope (the fallback must stay the *unmodified* legacy implementation, per
Clarifications). The fallback path keeps today's timing-based approximation unchanged. This is
called out here as a known, accepted gap in degraded mode, not silently dropped.

**Alternatives considered**:
- *Route fallback TTS audio through `MediaElementAudioSourceNode`/similar to also get a real
  analyser feed* — investigated and rejected: `speechSynthesis` does not expose its output as
  a capturable media element or stream in any standard browser today; there is no supported
  way to tap it, which is exactly why the original implementation resorted to the timing
  approximation in the first place.

## Decision 7: New endpoints join the existing `ai-endpoints` rate-limit policy

**Decision**: `POST /api/v1/ai/voice/stt-session`, `POST /api/v1/ai/voice/reply`, and the
voice preferences endpoints are added to `AiController` under its existing
`[EnableRateLimiting("ai-endpoints")]` attribute — no new policy.

**Rationale**: Same reasoning already recorded in specs/005-multi-provider-ai-engine's
contracts/chat.md for `/api/v1/ai/compare` — these are AI-invoking endpoints belonging to the
same controller and the same cost/abuse profile as `/chat`/`/transcriptions`/`/translate`/
`/images`; a second policy would add configuration surface with no behavioral difference.

## Decision 8: STT transient-reconnect vs. failover threshold

**Decision**: `useSpeechRecognition.ts` retries the ElevenLabs realtime STT WebSocket
connection up to 2 times, with a 1-second delay between attempts (≤~3 seconds total), before
treating the primary provider as down and invoking `voiceProviderStatus.ts`'s failover to the
fallback engine (Decision 5, FR-033). A reconnect that succeeds within this window is
invisible to the user and is **not** recorded as a `VoiceProviderFailoverEvent` — FR-004 is a
private, transient recovery; FR-033/FR-039 fire only once the retry budget is exhausted.

**Rationale**: FR-004 ("automatically attempt to recover from a transient loss... without
requiring the user to restart") and FR-033 ("switch to fallback when primary is unreachable")
describe two different severities of the same failure mode, but nothing previously defined
where one ends and the other begins, leaving the reconnect logic with no rule to implement. A
small, bounded retry budget keeps FR-004's "transient" promise without silently absorbing a
genuine outage that FR-033/SC-005 requires surfacing within 3 seconds — 3 retry-attempt
seconds plus the existing `stt-session` mint round-trip stays comfortably inside that ceiling.

**Alternatives considered**: Exponential backoff over a longer window — rejected: SC-005
already commits to a 3-second ceiling for surfacing fallback, so a longer retry budget risks
missing that target.

## Decision 9: Language resolution for STT/TTS

**Decision**: Both `CreateSpeechToTextSessionCommand` and `StreamVoiceReplyCommand` accept the
same `language` value `ChatPage.tsx` already tracks and passes to `useTextToSpeech.speak(text,
language)` today (existing code, unrelated to this feature). The STT session-mint call passes
it to ElevenLabs as the realtime STT connection's `language_code` parameter (confirmed field
name from the batch STT quickstart — `language_code="eng"`, "If set to None, the model will
detect the language automatically"; realtime-endpoint parity to be confirmed against the API
reference at implementation time, consistent with this document's existing residual
verification risk #1). TTS voice selection extends `ElevenLabsOptions` with a per-language
voice-id map, mirroring the existing `voicePersonaMap.ts` structure already used by the
fallback engine.

**Rationale**: FR-007/FR-012 require STT/TTS parity with every language the UI already
supports (Clarifications) — reusing the exact `language` value already threaded through
`ChatPage.tsx` for the legacy TTS path is the smallest change that achieves parity, and keeps
the primary and fallback persona/language selections structurally consistent with each other
(FR-009's fallback-parity clause).

**Alternatives considered**: Deriving language from `navigator.language`/browser locale
instead of the app's own `language` state — rejected: the app already has its own explicit
per-conversation language selection (`ChatPage.tsx`'s `language`/`onLanguageChange`), which
may differ from the browser's locale; using the app's own value keeps STT/TTS consistent with
what the user actually selected for text responses today.

## Decision 10: Client-side fast-path interruption detection

**Decision**: `useSpeechRecognition.ts` runs a cheap local amplitude-threshold check — reusing
`useWavRecorder.ts`'s existing peak-level ring-buffer technique — directly on the microphone's
`AudioWorkletNode` output, independent of ElevenLabs' round-tripped VAD event. Crossing the
threshold immediately (client-side, no network round trip) ducks/pauses AI playback via
`useVoiceAnalyzer`'s gain node (Decision 6, shared with the mute control) and moves
`useVoiceState` toward `Interrupted`. ElevenLabs' authoritative partial-transcript event, when
it arrives, either confirms the turn (finalizing cancellation and the transition to
`Listening`) or — if it never arrives (a false positive, e.g. a cough) — playback resumes from
the duck point rather than staying cancelled.

**Rationale**: SC-002's 300ms/95th-percentile target cannot reliably be met if "the user
started speaking" is only known after a full round trip to ElevenLabs' STT service — local
amplitude detection is near-instant and only needs to be a *fast, reversible* signal (duck,
not an irreversible cancel) since the authoritative server event still governs the real
interruption/cancellation, exactly the same trust split `useWavRecorder.ts` already uses
between its own local level metering and the eventual transcription result.

**Alternatives considered**:
- *Rely solely on ElevenLabs' VAD event* — rejected as the root cause of the risk this
  decision resolves.
- *A dedicated client-side VAD library* — rejected as unnecessary complexity; a simple
  peak-amplitude threshold (already implemented for `useWavRecorder.ts`'s waveform) is
  sufficient for a duck/pre-trigger signal, not a full speech/non-speech classifier.

## Residual verification risk (non-blocking)

Two items could not be confirmed from the pages fetched during planning and should be
verified against ElevenLabs' current API reference at the start of implementation, not
guessed at here:
1. **Exact TTS streaming endpoint path and request/response field names** — the specific
   `text-to-speech/{voice_id}/stream` reference page returned 404 at fetch time (the cookbook
   page itself only confirmed the model id `eleven_v3`, output format `mp3_44100_128`, and
   that official Python/TypeScript SDKs exist). Decision 3's design (buffer LLM text into
   chunks, POST to a streaming TTS endpoint, relay bytes) does not depend on the exact field
   names, so this doesn't block planning — it's a `/speckit-tasks`-time verification step.
2. **MyASP.NET inbound WebSocket support** — the fetched Node.js/React hosting guides describe
   `web.config`/`httpPlatformHandler`/static-file deployment but don't address WebSocket
   support one way or the other. Decisions 2 and 3 were deliberately chosen so this feature
   does not need an answer either way (STT is browser-to-vendor directly; TTS reuses existing
   chunked-HTTP streaming) — flagged here so a future feature that *does* need inbound
   WebSocket support on this host knows to verify it first.
3. **`language_code` support on the realtime STT endpoint specifically** (Decision 9) — only
   confirmed for the *batch* STT quickstart; the realtime/`Scribe.connect()` guide fetched
   during planning didn't show a language parameter one way or the other. If the realtime
   endpoint doesn't accept it, Decision 9's TTS-side per-language voice map still stands, but
   STT language hinting would need to fall back to ElevenLabs' automatic language detection
   for the primary path — verify at implementation time, not a planning blocker.
