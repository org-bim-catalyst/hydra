# Contract: `siteBoundaryEdit` stream event

This event is emitted during a chat turn when `edit_site_boundary` succeeds. It follows the same
path as `solarAnalysis`: `StructuredPayloadExtractor` → `ChatStreamChunk` → SSE `data:` frame
→ `useChatStream`.

## Server

```csharp
public sealed record SiteBoundaryEditCommand(Guid ChatId, Guid Revision);
// ChatStreamChunk gains: SiteBoundaryEditCommand? SiteBoundaryEdit = null
```

## Wire

```json
{ "type": "siteBoundaryEdit", "chatId": "…", "revision": "…" }
```

## Client handling (`useChatStream.ts`)

1. Ignore the event if `chatId` ≠ the chat on screen, the same rule as `clearUnlessShowing`.
2. If `activeSiteBoundaryStore.revision` ≠ `revision`, refetch chat detail first, so the editor
   never starts from a stale outline.
3. Call `siteBoundaryEditStore.enter({ chatId, revision, rings from activeSiteBoundaryStore })`,
   which runs the edit-mode entry in [edit-mode-viewer.md](edit-mode-viewer.md).
4. If entry fails (for example the map isn't ready), show a snackbar, "Couldn't open the
   outline editor — try the Edit control on the map." The failure is never silent (§2 VIII).

This event is not persisted. On reload, the persisted assistant narration stays, and the user
re-enters through the map control. That is correct, because edit mode is a client state.
