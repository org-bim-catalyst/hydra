# Data Model: Reply Action Bar

No persistent entities. This feature adds one piece of transient, component-local UI state and
reuses all playback state already defined in specs/039-composer-interaction-states-redesign's
`data-model.md` ("Assistant Reply Playback": `playingMessageId`, `isManualReplay`) unchanged.

## Copy Status (new, local to `MessageBubble`)

Per-message-bubble transient state, not lifted to `ChatPage.tsx` and not persisted anywhere —
each rendered bubble owns its own copy-confirmation state independently.

| Field | Type | Description |
|---|---|---|
| `copyStatus` | `'idle' \| 'success' \| 'error'` | Drives the Copy action's icon/tooltip. `'idle'` is the resting state (FR-001's default). Set to `'success'` or `'error'` synchronously when `navigator.clipboard.writeText` resolves/rejects (FR-002/FR-003), then reset to `'idle'` after a short timeout so the confirmation is "brief" (spec User Story 1). |

**Lifecycle**: `idle` → (click) → `writeText` pending → `success` or `error` → (timeout) →
`idle`. A new click while a prior confirmation is still visible immediately re-attempts the
copy and re-derives `copyStatus` from that new attempt (Edge Cases: back-to-back activations
are independent, never blocked by a stale confirmation).

## Reply Action Row (new, structural — not a data entity)

The row itself carries no state of its own; it is a rendering position (data-model.md,
specs/039) that now hosts two independent controls side by side:

- Replay/Stop — unchanged state machine, owned by `ChatPage.tsx` (see specs/039 data-model.md
  and contracts/reply-playback-control.md), only relocated visually.
- Copy — new, owned entirely by `MessageBubble.tsx` per-instance (`copyStatus` above); requires
  no new prop threading from `ChatPage.tsx` since it only ever needs `message.content`, which
  `MessageBubble` already receives.
