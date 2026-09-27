# Research: Restore Local Whisper as the Primary Dictation Engine

**Feature**: specs/078-restore-local-whisper | **Date**: 2026-09-27

Engine names used throughout (spec glossary): **Local Whisper** (Whisper.net, in-process, free),
**OpenAI Whisper** (OpenAI's hosted `whisper-1`), **ElevenLabs realtime** (ElevenLabs' hosted
live-dictation API, streaming only), **Browser built-in** (the Web
Speech API `SpeechRecognition`).

---

## D1 — Local Whisper runtime: restore Whisper.net 1.9.1, load lazily

**Decision**: Restore `Whisper.net` + `Whisper.net.Runtime` 1.9.1 (the exact versions removed in
`2c1717be`) in `AskLucy.Infrastructure`. The engine (`LocalWhisperTranscriber`) loads a
`WhisperFactory` lazily on the first clip, not at startup, and caches exactly one factory keyed by
model file path. When the selection changes, the next request loads the new model; the old factory
is disposed once its in-flight requests finish (lease count reaches zero). Processing uses
`CreateBuilder().WithLanguage(<user language>)` when a language hint is present, otherwise
`WithLanguageDetection()`. Concurrent transcriptions are capped by a `SemaphoreSlim`
(`LocalWhisper:MaxConcurrentTranscriptions`, default 2); a request that cannot get a slot within
`LocalWhisper:QueueTimeoutSeconds` (default 10) fails as `Unavailable` and the user falls to the
browser built-in (FR-005b).

**Rationale**: The removed implementation proved Whisper.net works on this host. Its one real cost
(commit `2c1717be`: ~200 MB loaded at every startup by `WhisperWarmupHostedService`) is avoided by
loading lazily — a site that never dictates never pays it. A per-path cache mirrors Supertonic's
"in-flight request finishes on the model it started with; the next one picks up the new one" (spec
Edge Cases). The concurrency cap stops a burst of clips from starving the shared site4now CPU.

**Native DLL check** (memory: PDFium collision): Whisper.net.Runtime ships `whisper.dll` and
`ggml*.dll`. No current package ships either name (current natives: onnxruntime, pdfium via
Docnet, tesseract/leptonica). The same set coexisted with Docnet, Tesseract and OnnxRuntime before
`2c1717be`, so there is no collision.

**Alternatives considered**:
- *Eager warm-up hosted service* (the removed design) — rejected: pays ~200 MB RAM on every restart.
- *whisper.cpp as a sidecar process* — rejected: a second deployable on a shared host with no
  process supervisor; in-process is what FR-001 asks for.
- *ONNX Whisper via the existing OnnxRuntime* — rejected: needs our own decoder/tokenizer loop and
  would not load the ggml files the Hugging Face ecosystem publishes for whisper.cpp.

## D2 — No model ships or downloads; a fresh deployment dictates through the browser built-in

**Decision**: Nothing is downloaded or activated automatically (user decision, 2026-09-27). A fresh
deployment has no Local Whisper model selected (`LocalWhisperModelId = null`), and the stt-session
call (D6) answers `Browser` for any path that would use Local Whisper. That is the normal path, not
a failure: no degraded notice, nothing on the trail. The administrator deploys the recommended
multilingual `ggml-base.bin` (~148 MB) through Custom Models with the single-file URL
`https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin` (D11) and selects it
(D12); from the next dictation on, Local Whisper serves. Configuration keeps only the runtime knobs
(`LocalWhisper:MaxConcurrentTranscriptions`, `LocalWhisper:QueueTimeoutSeconds`); there is no
`DefaultModelFile` and no provisioner. The loose `App_Data/whisper-models/ggml-BaseEn.bin` on
production is not a Custom Model, so it is neither picked up nor touched (FR-014).

**Rationale**: FR-002/FR-008/FR-010 as clarified: "the built-in browser API is used until the admin
defines more AI providers and adds local models". It also removes a background download on a shared
host with the short-timeout history (memory: site4now short-timeout pattern), and one code path —
Custom Models — becomes the only way a model file reaches the server.

**Alternatives considered**:
- *A shipped default auto-downloaded by a hosted provisioner* (the first plan) — rejected by the
  user's decision above.
- *Pick up the existing `ggml-BaseEn.bin`* — rejected: English-only (FR-008), and loose files
  bypass the Custom Models selection and ggml checks.
- *`ggml-base-q5_1.bin` (quantized, ~57 MB)* as the recommendation — rejected: noticeably worse on
  Arabic at the base size; an admin can still deploy and select it (US4).

## D3 — Audio conversion happens in the browser (FR-013)

**Decision**: The browser converts every dictation clip to 16 kHz mono 16-bit PCM WAV before
upload: `AudioContext.decodeAudioData(recordedBlob)` → `OfflineAudioContext(1, …, 16000)` render
(the browser's own resampler) → the existing `float32ToInt16Pcm` in `pcm16.ts` → a 44-byte RIFF
header. New module `features/chat/voice/wavEncoder.ts`, no new dependency. A 60 s clip (the
existing `MAX_UTTERANCE_MS`) is ~1.9 MB. The server validates the RIFF/WAVE header (PCM, 1 channel,
16000 Hz, 16-bit) and rejects anything else with 422, recorded on the failure trail as
`ValidationFailed` (FR-007). If the browser cannot decode its own recording, it uploads the raw
recording unchanged, so the server's validation records that failure too — the server is the only
place the trail can be written.

**Tradeoff (requested by the feature)**:

| | Browser conversion (chosen) | Server conversion |
|---|---|---|
| Server CPU | none | a decode per clip on the shared host |
| Dependencies | none (Web Audio API) | ffmpeg or a webm/opus decoder; ffmpeg cannot be relied on on site4now |
| Upload size | ~2× larger than opus (1.9 MB/min) | smallest |
| Failure visibility | needs the raw-upload trick above to reach the trail | natural |
| One format for both clip engines | yes — Local Whisper and OpenAI Whisper both accept WAV | yes |

**Rationale**: No server-side decoder is available on the shared host, and a managed webm/opus
decoder would be a new native dependency. Clip sizes stay well within the upload limit.

## D4 — Two clip engines; Push-to-Talk under ElevenLabs realtime uses a configured engine

**Decision**: A single Application port `IDictationClipTranscriber { DictationClipEngine Engine;
Task<DictationTranscript> TranscribeAsync(Stream wav, string? language, CancellationToken) }` with
two Infrastructure implementations:
- `LocalWhisperTranscriber` (D1).
- `OpenAiWhisperClipTranscriber` — an adapter over the existing `OpenAIProvider` transcription
  call (`whisper-1`).

`DictationEngineSetting.ResolveClipEngine()` decides which one a clip goes to: the primary engine
when it is Local Whisper or OpenAI Whisper; otherwise (ElevenLabs realtime primary, which only
streams) the administrator-chosen **Push-to-Talk engine** — Local Whisper by default, OpenAI
Whisper while OpenAI is switched on, or the browser built-in (FR-017). Nothing else chooses a
transcriber, and a transcriber failure only ever leads to the browser built-in (FR-005).

**Rationale**: Push-to-Talk always records a clip (FR-012) and ElevenLabs realtime cannot take one.
The user decided Push-to-Talk then uses Local Whisper unless the administrator decides otherwise.
It is a configured choice made in advance, not a failover. No new ElevenLabs batch call is added.

**Alternatives considered**:
- *An ElevenLabs batch clip transcriber* (the first plan) — rejected by the user's decision; it was
  also a new vendor call.
- *Push-to-Talk streams through the ElevenLabs realtime socket while held* — rejected: rewrites the
  SPEC-032 hold-to-talk capture path for one engine.

## D5 — A separate dictation clip endpoint; `/ai/transcriptions` stays for file uploads

**Decision**: New `POST /api/v1/ai/voice/transcriptions` (contracts/dictation-transcription.md)
→ `TranscribeDictationClipCommand`. The existing `POST /api/v1/ai/transcriptions` is unchanged: it
stays the file-attach upload in `ChatComposer`, served by OpenAI Whisper.

**Rationale**: The two have different rules. Dictation follows the admin's primary engine, the
suspension state and the WAV-only contract. File attach accepts any audio format and has no
browser fallback. Merging them would put a mode flag on one endpoint.

## D6 — The stt-session call doubles as the health pre-check (FR-005a)

**Decision**: `POST /api/v1/ai/voice/stt-session` stays the one "how do I dictate this turn?"
call, and both capture paths call it before opening the microphone. Push-to-Talk is new to this;
today it records straight away. `DictationEngine` becomes `Realtime | Clip | Browser`. `Whisper`
is renamed `Clip`, because the clip may go to Local Whisper or OpenAI Whisper.

First resolve the engine for this turn: Continuous → the primary; Push-to-Talk → the primary, or
the Push-to-Talk engine while ElevenLabs realtime is primary (D4). Then:

| Engine for this turn | Answer | When it can't serve |
|---|---|---|
| Suspended for this engine's vendor | `Browser`, no vendor call | — |
| Browser built-in (the configured Push-to-Talk engine) | `Browser`, not degraded | — |
| Local Whisper, no model selected or selected model Unavailable | `Browser`, not degraded, nothing on the trail | — |
| Local Whisper, selected Available model | `Clip` (file present, ggml magic) | `Browser` + failover on the trail |
| OpenAI Whisper | `Clip` (OpenAI switched on, credential resolvable) | `Browser` + failover on the trail |
| ElevenLabs realtime, Continuous | `Realtime` + token | `Browser` + failover on the trail |

The response carries `degraded: bool`, so the client shows the "browser built-in" notice only on a
real failure, never on the not-configured path.

The request gains `mode: "Continuous" | "PushToTalk"`, so ElevenLabs realtime does not mint a token
Push-to-Talk will never use. Health checks for Local Whisper and OpenAI Whisper are local checks,
not network calls. For ElevenLabs realtime in Continuous mode, minting the token *is* the check.

**Stale tabs**: a tab loaded before the deploy still posts to `/ai/transcriptions` (OpenAI
Whisper) until reloaded. That endpoint is unchanged, so the tab keeps working; if OpenAI is
switched off it gets an error and falls to the browser as it does today. This is acceptable for
the minutes a stale tab lives.

## D7 — The platform-wide setting is a new singleton aggregate

**Decision**: New Domain aggregate `DictationEngineSetting` (data-model.md), one row, created on
first read with `PrimaryEngine = LocalWhisper`, `PushToTalkEngine = LocalWhisper`,
`LocalWhisperModelId = null` (none selected — the browser built-in serves, D2), `State = Active`. It is separate from `VoiceProvider` (the text-to-speech ordering of specs/070):
different lifecycle and invariants, and dictation has no priority list.

**Alternatives considered**: a `VoiceProvider` row per dictation engine — rejected, because
priority/failover ordering is exactly what FR-005 forbids for dictation.

## D8 — Subscription lapse vs transient failure (FR-016) reuses the existing Critical classification

**Decision**: `IFailureClassifier.Classify(exception)` → `OperationalFailureKind`, then
`OperationalFailureSeverityPolicy.IsCritical(kind)` decides. The Critical kinds are
CredentialRejected, CredentialUnreadable, NotConfigured, QuotaExhausted and UsageRestricted.
A Critical kind from a cloud engine that is selected (OpenAI Whisper as primary or Push-to-Talk
engine, or ElevenLabs realtime as primary) → `setting.Suspend(engine, reason, now)`, recording the
lapsed engine. While Suspended, only paths that would use that engine go to the browser built-in;
Local Whisper as the Push-to-Talk engine keeps serving under an ElevenLabs suspension. Anything
else (Unavailable, RateLimited, ResponseNotUnderstood, TimedOut, …) affects only the current attempt
(browser built-in for the retry, FR-005b). Local Whisper failures never suspend.

**Rationale**: One definition of "needs an administrator" platform-wide. The trail already marks
exactly these kinds Critical, so the incident the admin sees and the suspension always agree.

## D9 — Administrator notification is in-app for now

**Decision**: "Notify administrators" (FR-016, SC-006) is delivered by what exists:
1. The failure reported through `IVoiceFailureReporter` is a Critical kind, so the trail opens a
   Critical incident and publishes `CriticalIncidentOpened`.
2. Admin → Voice shows a "Suspended — browser built-in in use" banner with reason and time.
3. The admin navigation shows a badge on Voice while the setting is Suspended.

Email/push waits for specs/067 (notifications hub, not built). It will subscribe to the existing
`CriticalIncidentOpened` seam, which today has no handler by design.

**Flag to the user**: no email is sent by this feature.

## D10 — Switching a vendor off reverts the primary (FR-015)

**Decision**: `UpdateAiProviderCommandHandler`, when a provider goes from enabled to disabled,
calls every registered `IAiProviderSwitchedOffObserver.OnSwitchedOffAsync(providerKey, actor, ct)`
before its single `SaveChangesAsync`. `DictationEngineSettingSwitchOffObserver` calls
`setting.RevertToLocalWhisper(DictationRevertReason.VendorSwitchedOff, providerKey, now)` when the
primary or the Push-to-Talk engine depends on that vendor; each one that does goes back to Local
Whisper. The revert also clears a suspension of that vendor. Switching a vendor *on* never selects
it: that stays an explicit administrator choice. One unit of work, so the
provider row and the dictation setting can never disagree.

**Rationale**: The AI Provider engine must not reference the dictation module (CLAUDE.md "modules
communicate through interfaces"). There is no domain-event dispatcher in the codebase; adding one
for a single reaction is disproportionate (Complexity Tracking).

## D11 — Custom Models deploys only the named file (FR-011)

**Decision**: Rename the parsed `HuggingFaceModelSource.IgnoredFilePath` to `FilePath` (its
meaning changes from "ignored" to "deployed alone") and persist it as `CustomModel.SourceFilePath`
(nullable, new migration). In `CustomModelDeploymentJob.DeployAsync`, when `SourceFilePath` is set,
the listing is filtered to that one path (case-sensitive, as Hugging Face paths are) *before*
`PlanTransfer`, so the size cap, reserved-name check and overwrite report all see just the one file.
If the path is not in the listing, the deployment fails with "The file <path> is not in <repo> at
<revision>." A URL with no file deploys the whole repository, exactly as today. The Add Custom Model
dialog copy changes from "the whole repository is deployed" to "only <file> is deployed".

## D12 — Selecting and trying a Local Whisper model (FR-009a/009b/009c/010)

**Decision**:
- `IHostedModelLocator` gains `ResolveByIdAsync(Guid customModelId)`, which returns the same
  `HostedModelResolution`. Local Whisper is selected by id, not by repository id: two different
  ggml files from the same repository are two deployments.
- Selectable = Completed, not deleted, `SourceFilePath` set, and the file at
  `<destination>/<SourceFilePath>` exists and starts with the ggml magic (`0x67676d6c`, stored
  little-endian as `lmgg`). Anything else is refused with the reason (for example "Deploy the model
  from a URL that names its .bin file").
- At transcription time, a selected deployment an administrator marked Unavailable resolves to
  the browser built-in as the normal path (FR-010), and the admin page says why. A selected,
  Available deployment whose file is gone or no longer starts with the ggml magic is a failure: the
  trail records it and the user gets the gentle repeat (FR-007).
- Removing the selected deployment is refused ("Select a different Local Whisper model first").
  The guard lives in `RemoveCustomModelCommandHandler`.
- **Try it**: `POST /api/v1/admin/voice/dictation/try` takes a WAV sample, a model choice and a
  language, and returns the transcript and elapsed milliseconds. It uses a transient factory that
  is not cached and is disposed after the call; one try runs at a time. It never touches the
  setting or the primary cache. A failure returns Problem Details with the reason; admins may see
  internal detail, end users never do. It is logged, not put on the trail, because no user's
  dictation failed.

**Gap to flag**: `CustomModel.Remove` today only allows Failed or Cancelled deployments, so a
Completed model can never be removed, and removal leaves files on the target. "Delete the old one
later" therefore needs specs/072 extended. This feature adds the selection guard (FR-009b) but not
Completed-model removal. `ggml-BaseEn.bin` is a loose file, not a Custom Model, so retiring it is a
manual FTP delete once the deployed `ggml-base.bin` is selected and confirmed working (FR-014).

## D13 — Gentle repeat (FR-005b)

**Decision**: When the clip endpoint returns a failure, or the local conversion throws, the
frontend shows a localized message ("Sorry, I missed that — could you say it again?"). It comes from a
new `gentleRepeat.ts` phrase map keyed by the dictation language (en/ar/es/fr/de), because the app
has no i18n framework: every other string is inline English, so only this user-facing phrase needs
the map. When voice replies are on it also speaks the message through the
existing `useVoiceOutput`, so it uses Lucy's persona voice with no per-locale browser default
(CLAUDE.md voice persona rule). The next attempt only is marked "browser built-in". In Continuous
mode, listening restarts automatically after the message finishes. In Push-to-Talk the next press
uses the browser built-in; the press after that calls stt-session again (D6).

## D14 — Failure trail wiring (FR-006/FR-007)

**Decision**: Both handlers use `IVoiceFailureReporter` with `VoiceOperations.Transcription` and a
`VoiceEngineIdentity` whose `ProviderName` is the engine's glossary name ("Local Whisper",
"OpenAI Whisper", "ElevenLabs realtime"), with the model file or model id as `Model`.
- A failure that led to the browser built-in → `ReportFailover(..., fallbackServed: true)`.
- A clip the server rejected (bad WAV) → `ReportFailure`.
- Success → `ReportServed` only; the trail records a recovery only if a failover is pending.

Reasons pass through `FailureReasonSanitizer`. The user-facing response is always the generic text.
