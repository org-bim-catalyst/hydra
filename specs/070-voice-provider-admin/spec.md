# Feature Specification: Voice Provider Administration & On-Server Voice

**Feature Branch**: `070-voice-provider-admin`
**Created**: 2026-09-23
**Status**: Implemented
**Input**: Replace ElevenLabs with the open-source Supertonic 3 as Lucy's voice, keeping ElevenLabs
as the failover. Administrator request, verbatim: "we need to add a tab for the voice (TTS
provider), there should be a drop down menu for all service providers and "+" button to add more,
upon the provider selection, the second row show a drop down list with all the voices available,
there should be a textbox with sample sentence and play button, when user click the play button
the selected voice speak the sentence for testing."

## User Scenarios & Testing

### User Story 1 — Audition a voice (P1)

An administrator opens **Admin → Voice**, picks a provider and then one of its voices, types a
sample sentence or keeps the default one, and presses **Play** to hear that voice speak it.

**Acceptance Scenarios**:

1. **Given** the page opens, **Then** the provider list has Lucy's current provider selected, and
   the voice list shows that provider's voices with Lucy's current voice selected.
2. **Given** a different provider is selected, **Then** the voice list shows only that provider's
   voices.
3. **Given** a voice and sentence, **When** Play is pressed, **Then** the sentence is synthesised by
   that provider only (no failover) and played in the browser. Play becomes Stop while it plays.
4. **Given** the provider cannot synthesise (model not installed, key rejected), **Then** the
   server's reason is shown on the page. Pressing Play never silently does nothing.
5. **Given** the sample language changes and the sentence has not been edited, **Then** the
   sentence is replaced with that language's sample.

### User Story 2 — Add a provider with "+" (P1)

**Acceptance Scenarios**:

1. **Given** the + button, **Then** a dialog lists only the engines installed on this server that
   have not been added yet. An engine that needs an API key asks for one; the key is optional and
   can be set later.
2. **When** the provider is added, **Then** it joins the end of the failover order and is selected.
3. **Given** an engine that runs on the server (Supertonic), **Then** the dialog never asks for a
   key, and the server rejects one if sent.

### User Story 3 — Make a voice Lucy's voice (P1)

**Acceptance Scenarios**:

1. **When** "Set as Lucy's voice" is pressed, **Then** that provider becomes priority 0 with the
   chosen voice as its default, and the others keep their relative order behind it.
2. Lucy's replies then use that voice. If it fails before any audio plays, the next provider in
   the order speaks, using its own chosen voice.

### Edge Cases

- No provider is configured: the page says so, and voice replies fall back to the browser's voice
  through the existing `audio-failed` path.
- A provider's stored key cannot be decrypted: the router treats that provider as failed and moves
  to the next one. Preview reports it as "replace the API key".
- An engine fails after audio has started: the error is rethrown rather than failed over, so the
  voice never switches mid-sentence.

## Requirements

- **FR-001** Voice engines are pluggable behind `ITextToSpeechEngine`, and every engine produces MP3.
- **FR-002** Supertonic 3 runs in-process (fp32 ONNX) with 32 languages, ten preset voices and MP3 output.
- **FR-003** The order of voice providers is stored as administrator data (`VoiceProviders`), not in configuration.
- **FR-004** Failover happens only before the first audio chunk. An engine that fails is skipped for
  the rest of the request.
- **FR-005** API keys are stored encrypted with Data Protection; only a vendor-style hint is returned.
- **FR-006** Every admin endpoint requires `admin.ai-providers.view` to read or `admin.ai-providers.manage` to write.
- **FR-007** Preview text is limited to 500 characters and must contain at least one letter or digit.
- **FR-008** A missing Supertonic model never stops the host. The request that needed it fails and
  the failure is logged.

## Licence obligations (OpenRAIL-M)

Before Supertonic could be Lucy's voice in production, two things had to be in place. The Terms of
Service had to pass on the model licence's use restrictions, and users had to be told the audio is
AI-generated. Both were added on 2026-09-24:

- A public `/terms` page reproduces Attachment A (a)–(m) as binding and links the licence. It
  is linked from the landing footer, the app footer, the account menu and Settings → Voice.
  Its home link returns to where the reader came from: the landing page when they followed the
  landing footer's link (which sets `FROM_LANDING_STATE`, `routes/viewLandingState.ts`), the
  Studio otherwise. `/privacy` behaves the same.
- The disclosure appears in the Continuous voice panel and in Settings → Voice.

The mapping from each licence clause to where it is met is in `docs/THIRD_PARTY_NOTICES.md`.
Lawyer review of `/terms` is still outstanding.

## Follow-up changes (2026-09-24)

- **ElevenLabs is a Frontier vendor.** It is listed under Admin → AI providers with OpenAI,
  Anthropic, Gemini and OpenRouter, with the key `elevenlabs` and a Speech kind. Migration
  `AddElevenLabsAiProvider` seeds it disabled. That vendor row is ElevenLabs' only API key and its
  on/off switch, for both TTS and live dictation. The per-voice-provider Enabled toggle was
  removed. Admin → Voice shows ElevenLabs as On or Off, with a link to AI providers. The AI
  providers table shows no kind badge beside its name: like every vendor, what it does shows in
  the Capabilities column (audio and streaming chips) once its models are synced.
- **Live dictation falls back.** If the ElevenLabs realtime STT session is refused or unreachable,
  Continuous mode dictates through Whisper instead (`/ai/transcriptions`, committed after a
  1.2 s pause). If Whisper can't record in this browser or fails server-side, it uses the
  browser's `SpeechRecognition`. A caption names the engine in use, and every failure is shown to
  the user (`features/chat/voice/dictationFallback.ts`).
- **Voice order.** Supertonic is Lucy's voice unless an administrator reorders Admin → Voice.
  Other configured engines (ElevenLabs) are tried next; when every engine fails, the client
  speaks with the browser's own voice.
- **The browser voice stays in persona.** The last-resort browser voice is chosen by
  `selectPersonaVoice` (specs/010): a curated name first, otherwise the language-matching voice
  whose name is a known female voice in Google, Windows, Edge or Apple catalogs, preferring
  Edge's "(Natural)" voices. Names are matched as whole words, and a known male voice is chosen
  only when it is the language's sole voice. This closes the gap where an uncurated language
  (Arabic) could fall back to a male voice.
