# Contract: Suggested Actions API & UI

**Feature**: 045-conversational-agent-runtime | **Layers**: `Web/Controllers/v1` → `ClientApp/features/chat`

Covers offer composition (FR-021a, FR-021b), action dispatch (FR-027 – FR-030), the per-user toggle (FR-032), and the option card's interaction and accessibility contract (FR-031).

---

## 0. What an offer may contain (FR-021a)

An offer is a **mixed** list, assembled from whatever is logically related to what just happened:

| Kind | Selecting it runs | Grounding | Example |
|---|---|---|---|
| `flowVariant` | a declared flow prefix | **absolute** — key must be a registered flow + variant, available this turn | "Focus and outline the site" |
| `capability` | one capability | **absolute** — key must be in the turn's available set; arguments must satisfy its schema | "Search my knowledge bases for it" |
| `followUp` | **nothing** — Lucy says or asks something | **structural + best-effort** (below) | "Give you more information about it" |
| `decline` | nothing | n/a — appended server-side, always last (FR-022) | "Nothing for now" |

There is no "Something else" row: the composer is live throughout (FR-031), so a row for free text would be redundant.

### Conversational follow-ups (FR-021b, FR-021c)

Follow-ups are **composed for the situation**, not drawn from a fixed list. The offer step composes them from:

1. **What Lucy has learned from comparable situations** — supplied by the memory subsystem: what this user, and turns like this one, went on to want.
2. **Clarification** — when the request was ambiguous, a follow-up may be a question that resolves it ("Did you mean Al Safa Park or Al Safa Park 2?"). This gives a home to the multi-candidate disambiguation case spec 035 US2 described and spec 037 left out of scope.
3. **Likely successors implied by the capability index** — where what was just done obviously precedes something else.
4. **A recommendation** Lucy judges worth raising.

**How they stay honest without a registry.** Three layers, in decreasing strength:

- **Structural (absolute).** Dispatch for `followUp` has **no capability path at all**. Selecting one re-prompts Lucy with the follow-up's own text and she answers; no tool, flow or capability can be reached. A badly-composed follow-up therefore cannot make the platform *act* — the worst case is Lucy saying something she then has to walk back, which is a quality problem rather than a correctness one.
- **Prompt-level.** The offer step is given the turn's capability index (FR-024a), and instructed that anything requiring *doing* must be proposed as a `capability` or `flowVariant` row — where the absolute check applies — never as a follow-up.
- **Validator (best-effort).** A follow-up whose phrasing promises platform work rather than talking is discarded and logged with its text and reason. This cannot be perfect: composed prose has no key to check. SC-002b states the accepted ceiling (≤2%) rather than pretending otherwise.

**Ordering** in the card: substantive rows in the order the offer step returned them, then `decline`. The decline is appended server-side and never requested from the model, so it cannot be forgotten.

---

## 1. Dispatching a selection

A selection is **not** a new endpoint. It re-enters the existing stream (research.md D7).

`POST /api/v1/ai/chat/stream` — request body gains one optional member:

```jsonc
{
  "chatId": "…", "messages": [ … ], "providerId": "…", "modelId": "…",
  "generationParameters": { … },

  // specs/045 FR-027 — present only when the user selected an offered action.
  "selectedAction": {
    "offeredByMessageId": "018f…",   // the assistant message that made the offer
    "kind": "flowVariant",           // flowVariant | capability | followUp | decline
    "key": "locate_a_place:full",    // flow:variant or capability key; omitted for followUp
    "text": null,                    // followUp only: the composed follow-up, echoed back
    "arguments": {}
  }
}
```

**Server handling**

1. `SendChatMessageCommandValidator` requires, when `selectedAction` is present: a non-empty `offeredByMessageId`, a recognised `kind`, and `arguments` to be a JSON object.
2. The handler loads the referenced message and refuses unless it is (a) in this chat, (b) `Role = Assistant`, (c) carrying an offer containing this `kind` + `key`, and (d) the **newest** unanswered offer in the chat → else `409 conversation-action-stale` (FR-029).
3. For `flowVariant` and `capability`, availability is re-checked against a **freshly built** `TurnContext` → else `409 conversation-action-unavailable` (FR-028). A `followUp` has nothing to re-check: it invokes nothing, so it can never become unavailable.
4. The user message persisted for this turn records the selection; its `Content` is the action's label, so the transcript reads naturally (FR-026/SC-009).
5. The orchestrator **skips the decision step** and runs the selection directly (FR-027):
   - `flowVariant` → runs that variant from step 1, with the full narration cadence (FR-051c).
   - `capability` → runs that capability, announced from its `AcknowledgementTemplate`.
   - `followUp` → runs **no capability**, and structurally cannot (FR-021c); Lucy answers from the echoed follow-up text, as an ordinary reply with no beats.

**`decline`**: steps 3–5 are skipped. The user message is persisted with the decline recorded, a brief assistant acknowledgement is returned, and no work runs (FR-022, US3 AC2). It also suppresses the next turn's offer (FR-025a.3).

**Typing instead of selecting** requires no client action: any ordinary message makes the prior offer no longer the newest unanswered one, so it renders inert (FR-030).

### Problem Details

| Type | Status | When |
|---|---|---|
| `conversation-action-stale` | 409 | The offer is not the newest unanswered one, or is not in this chat |
| `conversation-action-unavailable` | 409 | Preconditions no longer hold at dispatch time |
| `conversation-action-unknown` | 400 | `capabilityKey` matches no registered capability |

All three carry a human-readable `detail` the client renders inline verbatim.

---

## 2. Conversation preference

Mirrors `/panels/preferences` exactly (data-model.md §5).

```http
GET  /api/v1/chats/preferences      → 200 { "suggestedActionsEnabled": true }
PUT  /api/v1/chats/preferences      ← { "suggestedActionsEnabled": false }  → 200 (echoes saved state)
```

Both require authentication and act on the calling user only. A user with no row gets the default `true`.

**When disabled** (FR-032): the orchestrator skips the offer step entirely — no model call, no `__ACTIONS__` event. Turns still narrate their beats and still perform requested work. Offers already in history render as **plain text** (the question and labels as a bulleted list), never as interactive rows.

---

## 3. `SuggestedActionCard` component

`src/AskLucy.Web/ClientApp/src/features/chat/components/SuggestedActionCard.tsx`, rendered by `MessageBubble` when the message carries an offer.

### Props

```ts
interface SuggestedActionCardProps {
  question: string
  actions: SuggestedAction[]
  /** false → inert history rendering (an earlier offer, or the feature disabled). */
  isLive: boolean
  /** Rejected promises must be handled by the caller; the card never fires and forgets. */
  onSelect: (action: SuggestedAction) => Promise<void>
  isSubmitting: boolean
  /** Rendered as an inline Alert inside the card. */
  error: string | null
}
```

### Visual contract (UX reference: Claude Code's AskUserQuestion card)

- A topic chip header derived from the turn subject, then the question.
- One radio row per action: **label** in the row's primary text, **description** as secondary text beneath it.
- Radios, not buttons — the reference pattern is select-then-submit, so a mis-click is recoverable before it acts.
- Substantive rows first, then the decline row, styled as a plain option and visually distinct from the substantive ones.
- A submit control, disabled until a selection exists.
- **No free-text row.** The composer is live throughout (FR-031) — the card must never overlay, disable or steal focus from it, and a user who simply starts typing bypasses the card entirely, which makes an explicit "Something else" row redundant.

### Interaction contract

| Behaviour | Requirement |
|---|---|
| Keyboard | Arrow keys move within the radio group; `Enter`/`Space` selects; `Tab` reaches submit; `Esc` dismisses the card (FR-030) |
| Dismiss | Client-side only — dismissing hides the card; the offer remains in history and the user may still type |
| While submitting | Radios and submit disabled; the card shows a busy state; the composer stays enabled |
| Failure | `error` renders as an inline `Alert severity="error"` inside the card, and the card returns to its selectable state so the user can retry (constitution §2.VIII) |
| Inert (`isLive: false`) | Rendered as static text, no radios, no submit, not focusable |

### Accessibility

- The group uses `role="radiogroup"` with `aria-labelledby` pointing at the question.
- Each row's accessible name is the **label**; the description is associated via `aria-describedby`, so a screen reader announces the choice before the explanation.
- The busy state sets `aria-busy` on the card.
- Errors are announced via `role="alert"`.
- An axe test covers the live, submitting, error and inert states — this is a novel interaction pattern, which constitution §10 requires be covered by automated a11y checks.

### Voice

The card contributes nothing to spoken output on its own. `ChatPage` builds the spoken string from the question plus non-decline labels (see [turn-stream.md](./turn-stream.md) §6). Descriptions, keys and arguments are never spoken (FR-044).
