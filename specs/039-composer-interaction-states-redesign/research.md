# Phase 0 Research: Composer Interaction States Redesign

All items below were resolved by reading the existing implementation
(`src/AskLucy.Web/ClientApp/src/features/chat/`) rather than by speculative research — this
feature sits entirely on top of already-shipped voice/transcription/TTS infrastructure
(specs 012, 013, 026, 029, 031, 032, 033, 034). One genuine product-level ambiguity (the
continuous-conversation entry point) was resolved via `/speckit-clarify` and is recorded in
`spec.md`'s Clarifications section, not repeated here.

## Decision 1: Click-to-talk and hold-to-talk gesture logic is already implemented

**Decision**: Reuse `ChatComposer.tsx`'s existing tap-vs-hold gesture disambiguation
(`handleMicPointerDown`/`resolveGestureOnRelease`, `HOLD_THRESHOLD_MS = 350`) unchanged.
User Story 2 (click-to-talk) and User Story 3 (hold-to-talk) require no new gesture-handling
code — a release under 350ms already routes to the tap/review flow (`RecordingReviewControls`
with `RiCheckLine`/`RiCloseLine`, matching Figure 3 exactly); a release at or past 350ms
already finishes immediately via `onStopCapture()` with no review step (matching the new
Figure 9/hold-to-talk mockup exactly).

**Rationale**: `specs/033-hold-to-talk-and-echo-fix` and
`specs/034-transcription-crash-gesture-and-continuous-view` already built and hardened this
exact two-gesture-on-one-control model, including the `setPointerCapture` fix for a real
bug where a fast release could route to the wrong element. Rebuilding it would duplicate
tested logic and risk reintroducing that bug (constitution §2.III DRY, §18 "never duplicate
logic that already exists").

**Alternatives considered**: Building a separate, dedicated hold-to-talk control distinct
from the mic button (matching a literal reading of the new mockup's "no cancel/confirm
buttons" framing as if it were a different control) — rejected because the existing
single-control gesture model already produces that exact outcome for a hold, and a second
control would violate specs/029's single-mic-control consolidation for no behavioral gain.

## Decision 2: Composer action visibility becomes state-dependent (new behavior)

**Decision**: `ChatComposer.tsx` currently shows attach/insert-prompt/mic/mode-switch/send
simultaneously at all times (only hiding during active recording); the send button is
always mounted, merely disabled when empty. This feature changes that: attach, mic, and the
continuous-conversation control MUST be hidden entirely (not just visually irrelevant) as
soon as the text field is non-empty, replaced by the send action — per FR-002 and Figure 2.

**Rationale**: This is an explicit, unambiguous requirement of the source mockups (Figure 1
vs. Figure 2 show mutually exclusive control sets) and spec.md FR-002 — not a defect in the
current code, but a deliberate visual simplification this feature introduces.

**Alternatives considered**: Keep all controls always-mounted (today's behavior) and only
change icons — rejected, contradicts the mockups and FR-002 directly.

## Decision 3: Continuous-conversation entry reuses the persisted preference (one-click hybrid)

**Decision**: Per spec.md Clarifications, the continuous-conversation action reuses
`voicePreferencesStore`'s existing `conversationMode` field and its existing mode-switch
control (today rendered as `RiFingerprintLine`/`RiInfinityLine` in `ChatComposer.tsx` and
`CollapsedVoiceControls.tsx`), reskinned to `RiVoiceprintLine`. Activating it while in
`PushToTalk` mode MUST, in one action, call `voicePreferencesStore.update({ conversationMode:
'Continuous' })` **and** immediately call `onStartCapture()`/start listening — collapsing
today's two separate steps (switch mode, then separately click the mic) into one.
Deactivating (the exit/`stop-line` action) MUST symmetrically stop listening and switch the
preference back to `PushToTalk`.

**Rationale**: See spec.md Clarifications for the full trade-off discussion. This keeps the
Settings → Voice preference page, its backend field, and specs/029's single-mic-control
design intent all intact, while fixing the two-click friction the new mockup's Figure 4
(direct-to-listening) implies.

**Alternatives considered**: Two fully independent, stateless buttons with no persisted
mode (rejected — largest blast radius, discards a deliberate prior architecture decision
and the Settings UI/backend field with no functional benefit the spec calls for); reskin
only, keep two-click flow (rejected — doesn't match the Figure 4 mockup's one-click
behavior).

## Decision 4: Replay/stop targets `useVoiceOutput`, extended to address a specific message

**Decision**: `useVoiceOutput()` is already a single, page-level shared instance
(`ChatPage.tsx`) used today only to auto-speak the most recent assistant reply
(`tts.speak(last.content, language)` in a `useEffect` keyed on stream completion). This
feature extends its contract minimally: `speak` gains an optional `messageId` (or the
caller — `ChatPage.tsx` — tracks "currently playing message id" itself, calling the
existing `speak(text, language)`/`stop()` API unchanged and deriving each `MessageBubble`'s
button state by comparing its own `message.id` to that tracked id plus `tts.isSpeaking`).
Either shape keeps `useVoiceOutput`'s internals (ElevenLabs streaming, fallback,
`useVoiceAnalyzer`) untouched — the design choice is made in Phase 1 (`data-model.md`)
between the two; both are equally minimal and the actual selection has no product-behavior
impact, only an internal-API shape difference.

**Rationale**: Reusing the single shared TTS channel naturally satisfies FR-023 ("at most
one reply plays at a time") for free — there is structurally only one audio output pipeline
already. A second, parallel playback mechanism would risk two audio streams overlapping and
would duplicate `useVoiceOutput`'s already-nontrivial ElevenLabs/fallback/error-surfacing
logic (constitution §2.III DRY, §9 "no duplicate provider access paths").

**Alternatives considered**: A separate `useReplayAudio` hook per message bubble — rejected,
would either need its own ElevenLabs/fallback wiring (duplication) or awkwardly wrap the
same shared hook per-instance, and would not naturally enforce "only one plays at a time"
without extra coordination that the single shared instance already provides for free.

## Decision 5: Auto-speak-on-arrival and manual replay share the same "currently speaking" state

**Decision**: The existing auto-speak effect (`ChatPage.tsx` — speaks the newest reply as
soon as streaming ends) and the new manual replay both drive the *same* `tts.isSpeaking` /
"currently playing message id" state. Clicking replay on an older message while the newest
reply is still auto-speaking MUST stop the auto-speak playback first (already implied by
FR-023), and conversely a new incoming reply's auto-speak MUST be treated the same as any
other "start playback" call with respect to stopping whatever replay was in progress.

**Rationale**: The spec's edge cases and FR-023 treat "assistant reply playback" as one
unified concept ("Users can never have two assistant replies playing audio simultaneously")
without distinguishing auto-play from manual replay — they are the same underlying
capability (speak a given message's text) triggered two different ways.

**Alternatives considered**: Treating auto-speak-on-arrival as exempt from the "one at a
time" rule (e.g., letting a new reply's auto-speak queue behind an in-progress manual
replay) — rejected as inconsistent with FR-023's unqualified "at most one" requirement and
not suggested anywhere in the source doc/images.

## Decision 6: Replay language uses the conversation's current active language

**Decision**: `ChatMessage` carries no per-message language field. Replay reuses the same
`language` value `ChatPage.tsx` already passes to `tts.speak()` for auto-speak (the
conversation's current active language), rather than attempting to infer/store a per-message
language.

**Rationale**: Matches existing auto-speak behavior exactly (no regression, no new field);
introducing per-message language storage would be new scope not requested by the spec
(constitution §2.III YAGNI).

**Alternatives considered**: Storing/deriving a language per message — rejected, out of
scope; no requirement in spec.md calls for replaying a message in a *different* language
than the conversation's current one.

## Decision 7: Replay control eligibility — only stable, completed assistant messages

**Decision**: The replay control (FR-020) renders only for assistant messages with a
resolved `message.id` and that are not the currently-streaming message (i.e., streaming has
finished for that message). A message mid-stream (undefined `id`, `isStreaming` true) shows
no replay control yet.

**Rationale**: `ChatMessage.id` is documented as `undefined` only "for the brief window
between a live send and the trailing history-refetch event resolving it"
(`aiApi.ts`); a replay control needs a stable identity to track "which message is playing"
and stable final `content` to speak. This is the only reasonable reading of spec.md's "an
assistant reply exists" (User Story 5, Acceptance Scenario 1) — a still-streaming message is
not yet a complete "reply."

**Alternatives considered**: Showing a disabled replay control during streaming — rejected
as unnecessary scope; spec.md's edge cases don't call for this and streaming messages are
already visually distinct (no attribution chip, etc.).

## Decision 8: Collapsed widget (`CollapsedVoiceControls.tsx`) — icon parity only, no layout change

**Decision**: The mockups in `docs/images/` depict only the Expanded chat panel (header with
back-arrow, "Ask Lucy · Online", full composer). The Collapsed floating-widget surface
(`CollapsedVoiceControls.tsx`, vertical icon stack, no text field, no message bubbles) has no
corresponding mockup and no replay/message-list surface at all (`CollapsedChatControl.tsx`
renders no `MessageBubble`). This feature updates `CollapsedVoiceControls.tsx` only to keep
its `RiFingerprintLine`/`RiInfinityLine` mode icon consistent with `ChatComposer.tsx`'s new
`RiVoiceprintLine`/`RiInfinityLine` (design-system consistency, constitution §7), but does
**not** restructure its vertical layout, add hold-to-talk-specific visuals, or add a replay
control there — none of that is depicted or required for that surface.

**Rationale**: Scope discipline (constitution §2.III YAGNI) — the spec and its authoritative
images describe the Expanded panel; extending unrelated visual restructuring to the
Collapsed widget without a mockup or requirement would be inventing scope.

**Alternatives considered**: Leaving `CollapsedVoiceControls.tsx`'s icon untouched (still
`RiFingerprintLine`) — rejected, would leave the two surfaces visibly inconsistent for the
same underlying mode concept, a design-system violation (§7).
