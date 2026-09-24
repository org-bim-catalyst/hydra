# Contract: Offer Card Presentation and Voice Scope

**Feature**: `068-honest-retry-offer-card` | **Status**: Design

Presentation-only. No data-model change, no API change, no change to option semantics, decline handling or history rendering.

## 1. Width

Two caps compound today — the bubble's and the card's — producing roughly 56% of panel width, which is the reported symptom. Both are addressed.

| Element | Today | Change |
|---|---|---|
| Assistant message bubble | `maxWidth: '75%'` unconditionally | Conditional: **full usable width** when the message carries an offer; unchanged (75%) otherwise |
| Offer card | `maxWidth: '75%'` of the bubble | **Removed** — fills its container |

"Full usable width" means the full width of the chat panel's message area inside its existing padding. Not an overlay, and it does not escape the panel.

### Rules

- The treatment is keyed on **offer presence**, not on measured content length (FR-017). No measurement pass, and the rule is predictable to a user.
- It ends at the next message without an offer (FR-017, SC-006a) — an offer-carrying message and an ordinary reply can sit adjacent at different widths in the same transcript.
- Answered and historical offer cards get the same width behaviour, and stay non-interactive (FR-020).

### Accepted trade-off

Reply prose in an offer-carrying bubble also renders full width and loses its reading measure (FR-017a). Chosen explicitly during clarification; the prose must remain readable at that width.

## 2. Internal layout

Width alone does not fix the stacking — at ~56% even a short label stacks.

| Element | Requirement |
|---|---|
| Option row | Selection control, label and description share one horizontal band (FR-018) |
| Confirm action | Fully visible, label unwrapped, at every supported width (FR-019) |
| Long single label | Wraps **within** the card; never forces the card wider than the panel (the spec's long-label edge case) |
| Narrowest supported width | Degrades to a readable stacked layout — no clipping, no horizontal scrolling (FR-021, SC-007) |
| Resize / docked ↔ expanded | Re-renders to the new width without clipping (US3 scenario 5) |

**Measurable target**: at the default docked panel width, no option label and no confirm action renders at fewer than 4 words per line where the underlying text is longer than 4 words (SC-006).

## 3. Accessibility

Unchanged and explicitly protected. Keyboard operation, focus order and assistive-technology labelling must behave identically at every rendered width (FR-022), with no regression in the project's automated accessibility checks (SC-008).

## 4. Voice scope

### Today

Voice speaks the reply, then the offer's question followed by every non-decline option label. Descriptions, keys and arguments are already excluded.

### Change

Voice speaks the reply, then a **brief cue** that choices are available — for example *"I've put some choices on screen."*

| Spoken | Not spoken |
|---|---|
| Lucy's reply for the turn (FR-024) | The offer's question (FR-023) |
| A short localized cue (FR-024) | Every option label (FR-023) |
| | Option descriptions, confirm action (FR-023) |

### Rules

- The cue is **localized**, and the existing young-adult female voice persona is unaffected — this governs *what* is spoken, not which voice speaks it.
- The cue is **appended** to the reply already being spoken rather than interrupting it (the spec's mid-sentence edge case).
- A cue is always spoken. Silence would leave a voice-only user unaware a choice is waiting (the voice-only edge case) — that would trade one failure for another.
- **Duration target**: a turn ending with an offer speaks no longer than the same reply without an offer, plus the cue (SC-011).

### This is not an accessibility reduction

Text-to-speech is not a screen reader. The card's DOM is untouched, so its full question, options and descriptions remain reachable by assistive technology regardless of what was spoken (FR-025). FR-025 exists so this distinction is not lost in review.
