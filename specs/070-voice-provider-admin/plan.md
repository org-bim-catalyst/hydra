# Implementation Plan: Voice Provider Administration & On-Server Voice

**Spec**: [spec.md](spec.md) · **Data model**: [data-model.md](data-model.md) · **Contract**: [contracts/admin-voice.md](contracts/admin-voice.md)

## Design

| Layer | Pieces |
|---|---|
| Domain | `VoiceProvider` entity: priority-ordered, with an encrypted credential and a default voice |
| Application | `ITextToSpeechEngine`, `VoiceProviderRouter` (the `ITextToSpeechProvider`), `IVoiceProviderRepository`<br>Commands: `AddVoiceProvider`, `SetVoiceProviderCredential`, `SetPrimaryVoiceProvider`, `PreviewVoice`<br>Queries: `GetAdminVoiceProviders`, `GetVoiceEngines`, `GetVoiceProviderVoices` |
| Infrastructure | `ElevenLabsTextToSpeechEngine` (now one `ITextToSpeechEngine` among several)<br>`Supertonic/`: `SupertonicModel`, `SupertonicText`, `SupertonicTextToSpeechEngine`, `Mp3StreamEncoder` |
| Persistence | `VoiceProviderConfiguration`, `VoiceProviderRepository`<br>Migration `AddVoiceProviders`, which seeds ElevenLabs at priority 0 |
| Web | `AdminVoiceProvidersController` (`/api/v1/admin/voice`) |
| ClientApp | `AdminVoicePage` (`/admin/voice`), `AddVoiceProviderDialog`, `VoiceProviderCredentialDialog`, `useSampleAudioPlayer` |

## Decisions

1. **The router lives in Application and the engines in Infrastructure.** The rest of the voice
   pipeline still speaks through one `ITextToSpeechProvider`, so nothing downstream changed.
2. **Fail over only before the first chunk.** Switching voices mid-sentence is worse than the
   existing `audio-failed` → browser-voice path.
3. **fp32, not int8.** With dynamic int8 quantisation, some English and Arabic samples were
   unintelligible. fp32 adds about 450 MB of resident memory with the ONNX memory arena disabled.
4. **MP3 (GroovyMp3, managed code, 96 kbps mono).** MP3 is about 7× smaller than WAV and plays
   through the client's existing MediaSource `audio/mpeg` pipeline unchanged. Opus and Ogg were
   rejected because Safari's MediaSource support for them is unreliable.
5. **A separate `VoiceProviders` table, not `AIProviders`.** Voice providers have no model catalog
   and no health history, and they are ordered rather than enabled/disabled.
6. **Seed ElevenLabs only.** Production behaviour stays the same until an administrator adds
   Supertonic and makes it Lucy's voice.
7. **Model files live outside git.** `scripts/download-supertonic.ps1` installs them at a pinned
   revision and verifies each file's SHA-256. This is the same deployment approach as
   `App_Data/tessdata`.

## Verification

- Synthesis with the real model in English and Arabic; Whisper transcribed the English output exactly.
- Unit tests: `VoiceProviderTests`, `VoiceProviderRouterTests`, `VoiceProviderAdminCommandTests`,
  `Supertonic*Tests`, `Mp3StreamEncoderTests`, `ElevenLabsTextToSpeechEngineTests`.
- Integration tests: `AdminVoiceProvidersControllerTests` cover the 401/403 responses, admin
  pass-through, the engines list, 404 for an unknown engine and 400 for an unspeakable preview.
- Frontend tests: `AdminVoicePage.test.tsx` (8 tests) and `AdminVoicePage.a11y.test.tsx`.
