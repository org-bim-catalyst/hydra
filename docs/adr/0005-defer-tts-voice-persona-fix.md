# ADR-0005: Defer TTS voice-persona-consistency fix from the immersive 3D workspace redesign

**Status**: Accepted
**Date**: 2026-07-30
**Deciders**: Recorded during `/speckit-plan` and `/speckit-analyze` for `specs/006-immersive-3d-workspace`

## Context

Constitution §7 ("Voice output") requires: "Text-to-speech/voice generation MUST use a
consistent young-adult female voice persona — matching 'Lucy's brand identity — across
every supported language, not whichever default voice a browser/platform happens to
expose per locale... falling back to an arbitrary system default voice for a language
that lacks one is not acceptable."

The existing `useTextToSpeech.ts` (`features/chat/voice/useTextToSpeech.ts`) plays voice
output via the browser's native `window.speechSynthesis`, selecting a voice with
`window.speechSynthesis.getVoices().find(v => v.lang.startsWith(lang))` — i.e. whichever
voice the browser/OS happens to expose for that language, with no persona matching at
all. This is a pre-existing gap that predates feature 006.

Feature 006 (`specs/006-immersive-3d-workspace`) extends this exact hook to add an
`isSpeaking`/`intensity` envelope so the workspace's 3D sphere can react visually to
voice output (see `research.md` §3). While researching that change, it became clear that
a real Web Audio `AnalyserNode`-driven reaction (true frequency/amplitude analysis,
rather than an approximation from utterance timing events) would require replacing
`window.speechSynthesis` with a server-rendered audio stream played through an `<audio>`
element — and that same migration would, as a side effect, also let the platform enforce
a specific persona voice consistently across languages, closing this constitution gap.

## Decision

**Feature 006 does not fix the persona-consistency gap.** It extends
`useTextToSpeech.ts` only to add the `isSpeaking`/`intensity` signal (and a caller-visible
`onerror` path) needed for the sphere's reactive visuals, preserving the existing
(non-compliant) browser-default voice-selection logic unchanged.

## Consequences

- Constitution §7's persona requirement remains unmet for all TTS output across the
  platform, not just this feature's new sphere visuals — a pre-existing condition, not
  worsened by feature 006.
- The 3D sphere's audio-reactive deformation (feature 006, FR-018) is therefore also an
  approximation (utterance-timing envelope) rather than true audio analysis — see
  research.md §3 for that specific trade-off.
- **Follow-up required**: a dedicated task, owned separately from feature 006, to migrate
  voice output to a TTS provider/pipeline capable of a consistent persona voice across
  languages (e.g., a server-rendered audio stream), which would also enable true
  audio-reactive visualization as a secondary benefit. No owner or date has been assigned
  yet.

## Alternatives considered

- **Migrate TTS to a server-rendered audio pipeline as part of feature 006** — rejected:
  disproportionate backend/infrastructure scope increase (new provider selection, audio
  streaming or storage, cost) for what is otherwise a frontend layout redesign; not
  required by feature 006's specification.
- **Leave `useTextToSpeech.ts` untouched entirely** — not viable: feature 006 needs the
  `isSpeaking`/`intensity` signal from this exact hook to drive the sphere, so the file is
  modified regardless; declining to also fix the persona logic while already touching it
  was a deliberate scope decision, not an oversight (recorded here per §17 rather than
  left undocumented).
