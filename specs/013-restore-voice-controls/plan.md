# Implementation Plan: Restore Voice Output Mute & Input Mode Controls

**Branch**: `013-restore-voice-controls` | **Date**: 2026-08-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-restore-voice-controls/spec.md`

## Summary

Spec 012 (`012-elevenlabs-voice-engine`) already built a complete conversational voice
subsystem — `VoiceControlBar.tsx`, `useConversationAudio`/`useSpeechRecognition`/
`useVoiceAnalyzer`/`useVoiceState`, the `voicePreferencesStore` (mute + conversation-mode
persistence), and a full backend surface (`UserVoicePreference` domain entity, EF Core
migration, MediatR commands/queries, `/api/v1/ai/voice/*` endpoints) — in a single PR
(#277, commit `2fdd6e1`). That same PR wired only the simpler pieces (per-message
auto-speak via `useVoiceOutput`, one-shot dictate-into-textbox via `useWavRecorder` in
`ChatComposer.tsx`) into the live page. `VoiceControlBar.tsx` and the hooks/store it
depends on were left fully built and unit-tested but never imported anywhere outside
their own test files — an incomplete integration, not a deleted feature.

This plan restores the mute control (US1) and the push-to-talk/continuous-listening mode
control (US2) by finishing that integration — but **narrower** than "swap in the whole
conversational subsystem": `useConversationAudio` ties voice input directly to spoken
output (only replies to voice-initiated turns get spoken), which would silently regress
the existing, already-shipped behavior of speaking *every* completed reply aloud regardless
of whether it was typed or spoken (spec 000's legacy FR-006, still live in `ChatPage.tsx`
today). Instead: `useVoiceOutput` (the hook already driving that every-reply auto-speak
effect) is extended with mute awareness (US1); `ChatComposer`'s one-shot dictate button is
replaced with a mode-aware mic control built directly on `useSpeechRecognition`, feeding
the same `send()` path typed messages already use, so the (now mute-aware) auto-speak
effect keeps handling every reply's audio uniformly (US2). `VoiceControlBar.tsx`'s
markup/a11y pattern is reused with an adapted, smaller prop contract. This closes the four
behavioral gaps the clarification session identified (hold-and-toggle push-to-talk
activation, no mid-reply mute resume, continuous listening ignoring composer typing, and
blocking a mode switch mid-capture) without coupling input to output. No new backend work,
new persistence schema, or new REST endpoints are required — the existing
`/api/v1/ai/voice/preferences` contract already stores exactly `conversationMode` and
`isMuted`. See research.md Decision 1 for the full reasoning behind this correction from
the initially-considered "wire in `VoiceControlBar`/`useConversationAudio` wholesale"
approach.

## Technical Context

**Language/Version**: TypeScript (frontend, strict mode) — this feature is frontend-only;
no C#/.NET changes are needed.

**Primary Dependencies**: React 19, MUI (existing `VoiceControlBar.tsx`), Zustand
(`voicePreferencesStore.ts`), TanStack Query (unaffected), the existing `voiceApi.ts`
client for `/api/v1/ai/voice/preferences`.

**Storage**: N/A for this feature — reuses the `UserVoicePreference` table/entity and
`/api/v1/ai/voice/preferences` GET/PUT endpoints delivered by spec 012, unchanged.

**Testing**: Vitest + React Testing Library (existing `VoiceControlBar.test.tsx` and
`useSpeechRecognition.test.ts` as a base to extend, plus new `useVoiceOutput.test.ts` and
`ChatComposer.test.tsx`), jest-axe for accessibility (existing pattern in
`VoiceControlBar.test.tsx`). `useConversationAudio.test.ts` is untouched — that hook is not
used by this feature (research.md Decision 1).

**Target Platform**: Web (existing Ask Lucy React SPA), desktop + mobile breakpoints per
constitution §7.

**Project Type**: Web application — this plan touches only `src/AskLucy.Web/ClientApp`
(frontend). No `backend`/`Infrastructure` changes.

**Performance Goals**: SC-001 (mute takes effect within 1s), SC-003 (mode switch active for
the very next input) — both satisfied by synchronous state updates in the extended
`useVoiceOutput` (`speak()`/`stop()` gating, Decision 3) and `voicePreferencesStore.update()`
(Decision 4/6); no new perf-sensitive path is introduced.

**Constraints**: Must not regress `ChatComposer`'s file-attach/type-to-send flow (untouched
by this feature); must not reintroduce the ElevenLabs realtime STT wire-protocol risk
silently — see research.md Decision 8.

**Scale/Scope**: Two user stories (P1 mute, P2 mode switch), frontend-only, reusing an
existing, tested component and hook set — no new screens, no new data model.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **§2.III Simplicity First (DRY/YAGNI)** — PASS. Extending the already-built
  `useVoiceOutput` (mute) and reusing `useSpeechRecognition` directly (input mode) instead
  of writing new capture/mute logic from scratch is the DRY-compliant choice.
  `useConversationAudio` is deliberately **not** reused, even though it also already
  exists, because it couples input directly to output in a way that would regress
  existing behavior (research.md Decision 1) — reusing it anyway just to avoid a second
  mechanism would trade one DRY concern for a worse functional regression, which is not
  what §18 asks for.
- **§2.VIII No Silent Failures** — PASS, contingent on design. `useSpeechRecognition`
  already surfaces errors via `recognition.error`, and the extended `useVoiceOutput`
  surfaces errors via `tts.error`; this plan's wiring must route both into the same
  visible Snackbar/Alert pattern `ChatPage.tsx` already uses, not silently drop them.
  Tracked in Phase 1 design.
- **§7 UI Principles (Accessibility, Theming, State management)** — PASS, contingent on
  design. `VoiceControlBar.tsx` is already keyboard-operable per its existing a11y test;
  the new hold-vs-toggle activation and the mode-switch guard must preserve that (Phase 1).
  Client/UI state (mute, mode) already lives correctly in a Zustand store
  (`voicePreferencesStore`), not duplicated into TanStack Query — no violation.
- **§9 AI Principles (Streaming, Fallback providers)** — PASS / largely N/A. This feature
  does not use `useConversationAudio`'s streaming turn orchestration (research.md
  Decision 1) — output continues through the existing per-message `useVoiceOutput`/
  `synthesizeSpeech` path, which already streams and already has a documented
  fallback-provider path (`useVoiceProviderStatus`/`failOver`), reused unchanged.
- **§3 Architecture / Clean Architecture** — PASS / N/A. No backend layers touched; all
  work is within the existing `Frontend` project boundary, calling the existing public
  `/api/v1/ai/voice/*` HTTP contract only.

No violations requiring a Complexity Tracking entry.

**Post-Phase-1 re-check**: All "contingent on design" items above are now resolved by the
Phase 1 artifacts — error surfacing is specified in contracts/voice-control-integration.md
(reusing the existing visible-error patterns, no new silent-failure path introduced),
keyboard operability for hold/toggle is specified explicitly (Decision 3, research.md;
Scenario 2/3/4, quickstart.md), and no new Zustand/TanStack Query state duplication is
introduced (data-model.md's Relationships section). Gate remains PASS with no violations.

## Project Structure

### Documentation (this feature)

```text
specs/013-restore-voice-controls/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/features/chat/
├── components/
│   ├── VoiceControlBar.tsx          # MODIFIED — prop contract adapted (Decision 2, research.md);
│   │                                #   markup/icons/tooltips/keyboard pattern reused
│   ├── VoiceControlBar.test.tsx     # MODIFIED — updated for new props, hold/toggle, mode-switch guard
│   └── ChatComposer.tsx             # MODIFIED — one-shot useWavRecorder dictate button replaced by a
│                                     #   mode-aware mic control built on useSpeechRecognition
├── pages/
│   └── ChatPage.tsx                 # MODIFIED — auto-speak effect KEPT (still fires for every reply),
│                                     #   gated by useVoiceOutput's new isMuted; VoiceControlBar wired
│                                     #   in for mute + mode display
├── voice/
│   ├── useConversationAudio.ts      # NOT USED by this feature — left as-is, unchanged, available for
│   │                                #   a future full conversational-mode feature (out of scope here)
│   ├── useSpeechRecognition.ts      # EXISTING — instantiated in ConversationView
│   │                                #   (ChatPage.tsx) and passed down to ChatComposer's
│   │                                #   new mic control; add hold-vs-toggle activation entry points
│   ├── useVoiceAnalyzer.ts          # EXISTING — unchanged; its gain-mute mechanism is not reused here
│   │                                #   (Decision 3, research.md uses a simpler gate-on-speak() approach)
│   ├── useVoiceState.ts             # NOT USED by this feature — left as-is, unchanged
│   ├── voicePreferencesStore.ts     # EXISTING — unchanged (already matches FR-011's shape)
│   ├── useVoiceOutput.ts            # MODIFIED — add isMuted/toggleMute/setMuted, gate speak() while
│   │                                #   muted, stop() on mute-while-playing (Decision 3, research.md)
│   └── useWavRecorder.ts            # RETIRED from ChatComposer's live path (Decision 4, research.md)
└── api/
    └── voiceApi.ts                  # EXISTING — unchanged, already exposes
                                      #   getVoicePreferences/saveVoicePreferences

src/AskLucy.Web/                     # Backend — NOT MODIFIED by this feature
```

**Structure Decision**: Single existing web application (`src/AskLucy.Web/ClientApp` for
React frontend, `src/AskLucy.*` for the already-unmodified .NET backend). This feature adds
no new top-level directories; it rewires existing files under
`features/chat/{components,pages,voice}`.

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
