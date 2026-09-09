# Contract: Reply Action Bar (Copy + relocated Replay/Stop)

**Feature**: [../spec.md](../spec.md) | **Data model**: [../data-model.md](../data-model.md)
| Implements FR-001–FR-009.

Amends `MessageBubble.tsx`'s replay control from
[contracts/reply-playback-control.md](../../039-composer-interaction-states-redesign/contracts/reply-playback-control.md)
(specs/039). `ChatPage.tsx`'s `handleReplay`/`handleStopReplay`/`playingMessageId`/
`isManualReplay` state machine is **unchanged** (FR-008) — only `MessageBubble.tsx`'s
rendering changes. Types are illustrative, not final implementation code.

## `MessageBubble` (props — unchanged shape, FR-008)

No new props. `onReplay`/`onStopReplay`/`showStopIcon`/`isReplayDisabled` keep the exact
signatures from specs/039's contract. Copy needs no prop from the caller — it reads
`message.content`, already a prop.

## `MessageBubble` (internal — new)

```ts
type CopyStatus = 'idle' | 'success' | 'error'

// NEW local state, one instance per rendered bubble (data-model.md "Copy Status")
const [copyStatus, setCopyStatus] = useState<CopyStatus>('idle')

const handleCopy = useCallback(async () => {
  try {
    await navigator.clipboard.writeText(message.content)
    setCopyStatus('success')
  } catch {
    setCopyStatus('error')
  } finally {
    // "brief" confirmation (User Story 1) — not a persistent state
    window.setTimeout(() => setCopyStatus('idle'), 2000)
  }
}, [message.content])
```

**Contract guarantees**:

- `handleCopy` always resolves to a visible `copyStatus` transition — either branch of the
  `try/catch` sets it — so no copy attempt can complete silently (FR-003, CLAUDE.md Error
  Handling). The `catch` here is not a discard: it is what *produces* the user-visible failure
  state, satisfying the project rule that a caught exception must still reach the caller/user
  in an actionable form.
- Rendered only when `!isUser` (FR-001); no `MessageBubble` for a user message ever mounts this
  handler or icon.
- Only rendered once the message has a stable `id` (reuses the existing `showReplayControl`-style
  completed-message guard — FR-007); a still-streaming message shows no action row at all.

## Reply Action Row (layout — replaces the in-bubble absolute-positioned control)

```tsx
{/* Outside the message Paper, inside the same outer flex column that aligns the bubble.
    Renders whenever the message is a completed assistant reply (FR-001/FR-007), independent
    of whether a Replay control is offered by the caller (FR-009). */}
{!isUser && message.id && (
  <Stack direction="row" spacing={0.5} sx={{ mt: 0.5, justifyContent: 'flex-start' }}>
    {showReplayControl && (
      <Tooltip title={showStopIcon ? 'Stop' : 'Replay'}>
        <IconButton
          size="small"
          disabled={!showStopIcon && isReplayDisabled}
          onClick={() => (showStopIcon ? onStopReplay?.() : onReplay?.(message))}
          aria-label={showStopIcon ? 'Stop' : 'Replay'}
        >
          {showStopIcon ? <RiStopFill size={16} /> : <RiPlayFill size={16} />}
        </IconButton>
      </Tooltip>
    )}
    <Tooltip title={copyStatus === 'success' ? 'Copied' : copyStatus === 'error' ? 'Copy failed' : 'Copy'}>
      <IconButton size="small" onClick={handleCopy} aria-label="Copy">
        {copyStatus === 'success' ? <RiCheckLine size={16} /> : <RiFileCopyLine size={16} />}
      </IconButton>
    </Tooltip>
  </Stack>
)}
```

**Contract guarantees**:

- Left-aligned (`justifyContent: 'flex-start'`) beneath the bubble regardless of `isUser`
  bubble alignment — FR-005 — because it is a sibling row, not inside the right/left-aligned
  outer `Box`'s bubble itself.
- The bubble `Paper`'s `pb: 4` reservation for the old in-bubble control is removed — the row
  no longer overlaps message content (FR-004).
- Icon `size={16}` (vs. the prior implicit ~20px `fontSize="small"`) on every icon in the row —
  FR-006.
- When `showReplayControl` is `false` (caller didn't wire replay), the row still renders with
  only the Copy `IconButton` — FR-009.
- `copyStatus === 'error'` renders a distinct tooltip/aria-label text ("Copy failed"), not a
  silent reversion to the idle icon — FR-003.

## Everything else (unchanged)

`ChatPage.tsx`'s `handleReplay`/`handleStopReplay`/`playingMessageId`/`isManualReplay`/
`handleStartCapture` and the entire `useVoiceOutput` playback pipeline from specs/039 are
untouched by this feature (FR-008) — this contract only relocates and restyles the control
that consumes that state, and adds one new, entirely local Copy control beside it.
