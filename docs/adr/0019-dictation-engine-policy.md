# ADR 0019: Dictation Engine Policy — Local-First, Browser-Only Fallback

**Status:** Accepted

**Date:** 2026-09-29

**Feature:** [specs/078-restore-local-whisper](../../specs/078-restore-local-whisper/spec.md)

## Context

On 2026-09-27 (commit `2c1717be`) the self-hosted Whisper.net pipeline
(`WhisperLocalTranscriptionProvider`, `WhisperOptions`, `WhisperWarmupHostedService`,
`ITranscriptionProvider`, the `/ai/transcriptions/microphone` endpoint, and the `Whisper.net` /
`Whisper.net.Runtime` packages) was removed as "unused", in the mistaken belief that OpenAI's
hosted `whisper-1` (reached via `/ai/transcriptions`) already covered its role. They are two
different engines: one runs in-process on our own server for free; the other is a paid vendor
call. specs/078 restores the local engine and, while doing so, has to settle several policy
questions an administrator-configurable three-engine dictation stack raises that a single hosted
engine did not.

## Decisions

### Local Whisper is the default, and nothing is downloaded automatically

A fresh deployment ships no model and downloads none at startup or on first use. Until an
administrator deploys a ggml model through Custom Models (specs/072, amended by specs/078 to
support single-file deploys) and selects it, dictation runs on the browser's built-in
`SpeechRecognition`. This is the normal path, not a failure — no notice, nothing recorded. This
was an explicit user decision: it avoids a background download on a shared, resource-constrained
host with a history of short outbound timeouts (memory: site4now short-timeout pattern), and it
means Custom Models becomes the single path any model file reaches the server by, so a file that
somehow appears in `App_Data/whisper-models` outside Custom Models (as `ggml-BaseEn.bin` still
does) is never picked up or touched.

**Alternative rejected**: a shipped default, auto-downloaded by a hosted provisioner on first
start. Rejected by the user directly.

### The browser built-in is the only fallback — cloud engines never fail over to each other

Whichever engine is primary (Local Whisper, OpenAI Whisper, or ElevenLabs realtime), any failure
of any kind falls back to the browser's own `SpeechRecognition` and nothing else. OpenAI Whisper
never fails over to ElevenLabs realtime or vice versa. This keeps the failure surface the
administrator has to reason about to one question — "did the chosen engine work for this
turn?" — instead of a priority-ordered chain (as TTS in specs/070 has). It also means a paid vendor
is never silently substituted for another paid vendor without the administrator's knowledge.

**Alternative rejected**: mirror the TTS voice-provider priority list (specs/070) for dictation.
Rejected because FR-005 explicitly forbids failover ordering for dictation: a cloud engine failing
over to another cloud engine could double a paid vendor's bill for a single turn without any
administrator action.

### The Push-to-Talk engine is a configured choice, not a failover, and only matters under ElevenLabs realtime

Push-to-Talk always records a full clip; ElevenLabs realtime only streams and cannot transcribe a
clip. So when ElevenLabs realtime is primary, an administrator separately picks which clip engine
(Local Whisper, OpenAI Whisper, or the browser built-in) serves Push-to-Talk. This is a decision
made in advance under Admin → Voice, distinct from the runtime browser-only fallback above: it is
what a Push-to-Talk clip is *routed to* by design, not what happens after the primary fails.

**Alternative rejected**: an ElevenLabs batch (non-realtime) transcription call for clips. Rejected
because it is a new vendor call the feature didn't ask for, and because Push-to-Talk streaming
through the realtime socket while held would have required rewriting the existing hold-to-talk
capture path (SPEC-032) for one engine only.

### Suspend (a vendor subscription lapsing) is distinct from revert (an administrator switching a vendor off)

Two different triggers produce two different outcomes, both landing on the browser built-in:

- **Suspend**: a Critical-kind operational failure (credential rejected/unreadable, not
  configured, quota exhausted, usage restricted — the same classification the operational
  failure trail, specs/074, already uses) from a *selected* cloud engine suspends that engine to
  the browser built-in and opens a Critical incident with an admin banner and nav badge. This is
  an unplanned lapse — the administrator didn't ask for it, so it needs their attention.
- **Revert**: an administrator explicitly switching a vendor off under Admin → AI providers
  reverts any primary or Push-to-Talk choice that depended on it back to Local Whisper, and
  records why. This is deliberate, planned, and needs no incident — just a visible notice on
  Admin → Voice of what changed and when.

Reusing one classification (Critical) for suspend keeps the incident the administrator sees and
the reason dictation stopped using an engine always in agreement, rather than inventing a second,
dictation-specific severity policy.

### A cross-module observer, not a domain event, carries the switch-off signal

`UpdateAiProviderCommandHandler`, on the enabled→disabled edge, calls every registered
`IAiProviderSwitchedOffObserver.OnSwitchedOffAsync(providerKey, actor, now, ct)` before its own
`SaveChangesAsync`, so the provider row and any dependent dictation setting change commit
atomically in one unit of work. `DictationEngineSettingSwitchOffObserver` is one such observer.

**Alternative rejected**: a domain event dispatched from the AI Provider aggregate. Rejected
because the codebase has no domain-event dispatcher today, and building one for a single reaction
would be disproportionate (constitution's Complexity Tracking) — the interface-based observer
achieves the same "the AI Provider module never references the dictation module" boundary
(CLAUDE.md: "modules must communicate through interfaces and application services rather than
directly referencing one another") without new infrastructure.

### Audio conversion to WAV happens in the browser, not on the server

The browser converts every dictation clip to 16 kHz mono 16-bit PCM WAV
(`AudioContext.decodeAudioData` → `OfflineAudioContext` resample → `float32ToInt16Pcm` → a RIFF
header) before upload, reusing the existing `pcm16.ts` encoder. The server validates the RIFF/WAVE
header and rejects anything else.

| | Browser conversion (chosen) | Server conversion |
|---|---|---|
| Server CPU | none | a decode per clip on a shared, resource-constrained host |
| New dependency | none (Web Audio API) | ffmpeg or a webm/opus decoder — not reliably available on site4now |
| Upload size | ~2× an opus original (about 1.9 MB/minute) | smallest |
| One format serving both clip engines | yes — Local Whisper and OpenAI Whisper both accept WAV | yes |

**Rationale**: no server-side audio decoder is available on the shared host, and adding one would
be a new native dependency with its own collision/versioning risk (memory: PDFium native DLL
collision is exactly this class of problem). Clip sizes stay well inside the existing upload
limit, so the tradeoff costs nothing observable to the user.

## Consequences

- Administrators must deploy and select a Local Whisper model themselves before it serves; there
  is no zero-config multilingual dictation out of the box. This is accepted as the cost of not
  downloading anything automatically on a constrained host.
- A dictation failure is always visible to the end user only as "browser built-in in use", never
  as which cloud vendor failed — vendor detail stays server-side (specs/074 failure trail), per
  the platform's no-silent-failure and no-vendor-leak rules.
- The `IAiProviderSwitchedOffObserver` seam is available to any future module that needs to react
  to a vendor being switched off, without a domain-event dispatcher existing yet.

## Related

- [specs/078-restore-local-whisper/research.md](../../specs/078-restore-local-whisper/research.md) — full decision log (D1–D14).
- [specs/078-restore-local-whisper/contracts/dictation-session.md](../../specs/078-restore-local-whisper/contracts/dictation-session.md)
- [specs/078-restore-local-whisper/contracts/admin-dictation.md](../../specs/078-restore-local-whisper/contracts/admin-dictation.md)
- [ADR 0008: AI provider failure classification](0008-ai-provider-failure-classification.md) — the Critical-kind classification suspend reuses.
- [ADR 0017: Operational failure trail](0017-operational-failure-trail.md)
- [ADR 0016: Custom Model deployment's temporary FTP target](0016-custom-model-deployment-temporary-ftp-target.md) — amended by specs/078 for single-file deploys.
