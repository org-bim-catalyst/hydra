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

## Go-live blockers (OpenRAIL-M)

Two things must be in place before Supertonic becomes Lucy's voice in production: the Terms of
Service must pass on the model licence's use restrictions, and users must be told the audio is
AI-generated. See `docs/THIRD_PARTY_NOTICES.md`.
