# UI Contract: Composer & Panel Layout

Scope: `ChatComposer.tsx`, `ExpandedChatPanel.tsx`, `chatPanelSizeStore.ts`. This is an internal
component/DOM contract (no public HTTP API), documented so implementation and tests target the
same shape.

## `ChatComposer` structure (User Stories 1 & 2)

```
<Paper column>                              borderRadius: radius.lg, NOT radius.pill
  <input type="file" hidden />
  <Box>                                     text-entry region
    <TextField multiline maxRows={6} .../>  fixed lineHeight; internal scroll past 6 rows
  </Box>
  <Stack direction="row">                   footer row — fixed at the bottom, always visible
    IconButton  Attach file            (Tooltip + aria-label, unchanged behavior)
    IconButton  Insert saved prompt    (Tooltip + aria-label; omitted when no onInsertPromptClick)
    IconButton  mic OR RecordingReviewControls   (Tooltip reuses dynamic aria-label)
    IconButton  Voice input mode settings        (existing Tooltip, unchanged)
    [optional]  voice-preferences-unavailable indicator (existing Tooltip, unchanged)
    IconButton  Mute/Unmute Lucy                  (existing Tooltip, unchanged)
    IconButton  Translate last response           (existing Tooltip, unchanged)
    IconButton  Send message           (Tooltip; wrapped in <span> while disabled)
  </Stack>
</Paper>
```

Invariants:
- The footer `Stack` never scrolls, shrinks, or reflows in response to the `TextField`'s content
  or scroll state — it has a fixed position at the bottom of the `Paper` at all times.
- Growing from 1 to 6 lines increases the `Paper`'s overall height; content beyond line 6 scrolls
  inside the `TextField` without further increasing the `Paper`'s height.
- Every button's control identity (aria-label, click handler, disabled condition) from
  specs/029-fix-chat-widget-bugs is unchanged — only its Tooltip presence and its position within
  the new column/footer grouping are new.

## `ExpandedChatPanel` structure (User Stories 3 & 4)

```
<Box column>
  <Stack direction="row">                    header — unchanged left-to-right order, plus one
                                              new button
    IconButton  Collapse                (Tooltip added; existing aria-label reused)
    LucyPortrait + name/status
    ActiveLanguageFlag
    IconButton  Start new conversation  (Tooltip added; existing aria-label reused)
    IconButton  <resize/toggle>         NEW — immediately after "Start new conversation",
                                         before headerTrailing. aria-label/Tooltip text:
                                         "Expand to full height" (when isFullHeight is false) /
                                         "Collapse to half height" (when isFullHeight is true).
                                         Icon: RiExpandVerticalLine / RiCollapseVerticalLine.
    {headerTrailing}
  </Stack>
  <Box>                                       content (children) — unchanged
</Box>
```

Height contract:
- `isFullHeight === false` (default): `height: { xs: 'min(70vh, 600px)', sm: 560 }` — unchanged
  from specs/029-fix-chat-widget-bugs.
- `isFullHeight === true`: `height: { xs: 'calc(100vh - 32px)', sm: 'calc(100vh - 48px)' }`
  (research.md Decision 3).
- `width` is unaffected by `isFullHeight` in either state.
- The resize/toggle button's `onClick` calls the caller-supplied `onToggleHeight`; the panel
  itself is otherwise stateless with respect to sizing (`isFullHeight` is a prop, not local state)
  so the caller (`ConversationView`) can source it from `chatPanelSizeStore`.

## `chatPanelSizeStore`

```ts
interface ChatPanelSizeState {
  isFullHeight: boolean   // default false
  toggle: () => void
}
```

- `localStorage` key: `ask-lucy-chat-panel-size`.
- No network calls, no error state, no loading state — see data-model.md.
