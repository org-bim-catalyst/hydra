# Quickstart: Validating the Composer Interaction States Redesign

## Prerequisites

- Backend running (existing chat/transcription/TTS endpoints — no changes needed for this
  feature).
- Frontend dev server: from `src/AskLucy.Web/ClientApp`, run `npm run dev`.
- A browser with microphone permission available (Chrome/Edge recommended for
  `SpeechSynthesis`/`getUserMedia` parity with existing voice specs' test notes).
- An existing conversation with at least one prior assistant reply (for User Story 5).

## Automated checks (run first)

```bash
cd src/AskLucy.Web/ClientApp
npm run test        # Vitest — component + a11y test files for every changed component
npx tsc -b --noEmit  # project-references-aware type check (bare `tsc --noEmit` checks nothing here)
npm run lint
```

All must pass before manual validation. Per constitution §10, new/changed behavior must have
corresponding test coverage — the automated suite is the primary gate; the scenarios below
are for visual/interaction confirmation the mockups' exact appearance can't be asserted by
Vitest alone.

## Manual validation scenarios

Each scenario references its spec.md User Story and the mockup figure it should visually
match (`docs/images/figure-image-N.png`).

### US1 — Compose and send (Figure 1 → 2 → 1)

1. Open a conversation. **Expect**: attach, mic, voiceprint icons visible; no send icon
   (Figure 1).
2. Type one character. **Expect**: attach/mic/voiceprint disappear, send icon appears,
   disabled while field is otherwise empty is N/A here since it now has text — send is
   enabled (Figure 2).
3. Clear the text field manually (select-all, delete). **Expect**: returns to Figure 1.
4. Type text again, click send. **Expect**: message sends, field clears, returns to Figure 1.

### US2 — Click-to-talk (Figure 1/2 → 3 → 1)

1. From Figure 1, **click** (not hold) the mic icon. **Expect**: waveform + close-line +
   check-line appear (Figure 3), no other composer icons.
2. Click close-line. **Expect**: recording discarded, no text added, back to Figure 1.
3. Repeat, speak a short phrase, click check-line. **Expect**: transcribed text appended to
   the field, composer returns to Figure 1 (or Figure 2 if text remains, per FR-007).

### US3 — Hold-to-talk (Figure 1 → 9 → 2)

1. From Figure 1, press and **hold** the mic icon (mouse down, don't release) for over
   ~500ms. **Expect**: icon becomes `mic-fill`, waveform shows, no close/check buttons
   (Figure 9).
2. Speak, then release. **Expect**: recording stops immediately, transcribed text lands in
   the field, composer shows Figure 2 (send enabled).
3. Send or clear the text. **Expect**: returns to Figure 1.
4. Regression check: press and release almost instantly (<350ms). **Expect**: this is
   treated as click-to-talk (Figure 3's cancel/confirm appear), not hold-to-talk — confirms
   research.md Decision 1/the edge case in spec.md is preserved.

### US4 — Continuous conversation (Figure 1 → 4/6 → 5 → 4/6 → 1)

1. From Figure 1, click the voiceprint icon **once**. **Expect** (per Clarifications'
   one-click hybrid): listening starts immediately — no second click needed — Lucy's
   circular avatar appears in the conversation view, composer shows mic-off-line + stop-line
   (Figure 4/6).
2. Click mic-off-line. **Expect**: input mutes, icon reflects muted state; click again to
   unmute.
3. Type a message while still in this state. **Expect**: send icon appears (Figure 5), Lucy's
   avatar/listening indicator remains.
4. Send it. **Expect**: message added to conversation, composer returns to Figure 4/6 (still
   in Continuous mode, avatar still showing).
5. Click stop-line. **Expect**: Continuous mode exits, avatar disappears, composer returns to
   Figure 1.
6. Regression check: open Settings → Voice. **Expect**: the mode preference reflects
   `PushToTalk` again after step 5 (confirms the persisted-preference reuse, not a
   parallel/independent toggle — research.md Decision 3).

### US5 — Replay a reply (Figure 8)

1. Send a message that produces a spoken reply; let it finish speaking naturally. **Expect**:
   a play-fill icon appears in the reply's lower-right corner once speech ends.
2. Click it. **Expect**: audio replays from the start, icon becomes stop-fill.
3. While it's playing, click replay on a **different** prior reply. **Expect**: the first
   reply's playback stops (its icon reverts to play-fill) before the second reply's audio
   starts (its icon becomes stop-fill) — confirms FR-023 (never two simultaneous).
4. Click stop-fill mid-playback. **Expect**: playback stops immediately, icon reverts to
   play-fill.
5. Click replay again on that same reply. **Expect**: playback restarts from the very
   beginning, not from where it stopped (FR-025).
6. Mute audio (header speaker icon). **Expect**: every reply's replay control becomes
   disabled while muted.

### US6 — Chrome cleanup

1. Inspect every composer state reached above (Figures 1–6, 9). **Expect**: no
   article-line/saved-prompts icon in any of them.
2. In the Expanded panel header, click the height-toggle icon. **Expect**: icon is
   `expand-diagonal-line`/`collapse-diagonal-line` (not the old vertical variants), and the
   panel still resizes correctly.

### Edge cases (spot-check at least these three)

- Deny microphone permission mid-recording (browser prompt or revoke via site settings).
  **Expect**: visible error (existing `Alert`/`Snackbar`), composer returns to Figure 1, not
  stuck showing a recording indicator.
- Start a click-to-talk recording, then click the voiceprint icon before confirming/
  cancelling. **Expect**: the in-progress recording resolves (cancels or completes) before
  Continuous mode starts — never runs in the background.
- With a reply mid-stream (before it finishes), confirm no replay control appears on it yet
  (only on completed replies).

## Success criteria mapping

Each scenario above exercises at least one of spec.md's SC-001–SC-007; a full manual pass
through US1–US6 plus the edge-case spot-checks is the acceptance bar for this feature before
merge, alongside the automated suite from "Automated checks" above.
