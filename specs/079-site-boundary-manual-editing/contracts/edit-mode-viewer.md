# Contract: Edit Mode in the Studio Viewer

This is a client-only contract. The state shape is in [data-model.md](../data-model.md); the
decisions are research D1, D2 and D10.

## Entry points

| Trigger | Condition |
|---------|-----------|
| Offer row "Edit the outline", or asking Lucy | The `siteBoundaryEdit` SSE event |
| Map control "Edit outline" (`SiteBoundaryOverlay`) | The outline is on screen, the chat is owned by the viewer, and no session is open |

Chats with no outline, and shared chats viewed by a non-owner, render no Edit control (spec edge
cases).

## `enter(chatId, revision, rings)`

1. Capture `ViewState`: `viewerEngineStore.camera.{mode, rotationEnabled}` plus the map's
   `getCenter()`, `getZoom()`, `getHeading()` and `getTilt()`.
2. `setCamera({ rotationEnabled: false })`, then `setCamera({ mode: 'plan' })`, then heading 0,
   then `fitBounds(all rings, 48)`. When `prefers-reduced-motion` is set, this is instant.
3. `GoogleMapsGisLayerHandle.setOutlineVisible(false)`. The animated rings hide and the tick
   stays running.
4. `editablePolygonController.mount(map, rings)`: one `google.maps.Polygon` per ring. The active
   ring is `editable: true`; the others are `editable: false` and dimmed. `zIndex` is 20, above
   the fallback polygon's 10.
5. Show `SiteBoundaryEditToolbar` and `SiteBoundaryCornerNavigator`. Disable the 3D/plan and
   rotation controls, with the tooltip "Finish editing the outline first".

## Toolbar (`SiteBoundaryEditToolbar`, MUI `Paper` + `Toolbar`)

- Title: "Editing: {siteName}".
- Area: "about {n} m²".
- Buttons: Undo and Redo (disabled when their stack is empty), Cancel, and **Done**, the primary
  button, disabled while saving or when the rings are unchanged.
- Status line, shown as needed:
  - a refusal reason (auto-clears after 4 s);
  - "Saving…";
  - an error `Alert` with **Retry**;
  - a conflict `Alert`, "The outline changed in another tab.", with **Load latest** and **Cancel**.
- `role="toolbar"`, `aria-label="Outline editor"`. All buttons are at least 44×44 px.

## `exit('done' | 'cancel' | 'forced')`

1. `editablePolygonController.unmount()`.
2. **done**: the outline store is already updated from the save response. Then call
   `setOutlineVisible(true)` and `setSiteBoundary(new outline)`. `SiteBoundaryRenderer` plays its
   normal draw-in animation, which is "Lucy applies the new outline with effects".
3. **cancel** or **forced**: `setOutlineVisible(true)`. The outline store was never changed.
4. Restore the view state: `setCamera({ mode })`, `moveCamera({center, zoom, heading, tilt})`
   under `cameraRestoreGuard`, then `setCamera({ rotationEnabled })` last.
5. **forced** (a different site arrived, FR-030): show a snackbar, "Your unsaved outline changes
   were dropped because a new site was shown."

## Local validation (`ringGeometry.ts`)

A change is refused, and the path reverted, when:

| Check | Message |
|-------|---------|
| The ring would have fewer than 3 corners | "An outline needs at least 3 corners." |
| The changed corner's edges cross another edge | "That would make the outline cross itself." |
| The ring area is below 1 m² | "That would make the outline too small." |
| Two corners are within 0.05 m of each other | "Two corners can't be in the same spot." |

## Keyboard (inside `SiteBoundaryCornerNavigator`)

| Key | Action |
|-----|--------|
| Tab / Shift+Tab | Next or previous corner. Tab past the last corner leaves the region. |
| `[` / `]` | Previous or next ring |
| Arrow keys | Move the corner 0.5 m. With Shift, 5 m. |
| Insert or `+` | Add a corner midway to the next corner |
| Delete or Backspace | Delete the corner |
| Ctrl/Cmd+Z, Ctrl/Cmd+Shift+Z or Ctrl+Y | Undo, redo |
| Escape | Cancel (with a confirm when there are unsaved changes) |

Announcements use a polite live region: "Corner {i} of {n}, {ring name}", then any refusal
message.

## Navigation and chat switching

- Leaving `/studio` keeps the session, and the editable polygons re-mount on return.
- Selecting another chat while the session has changes opens an MUI dialog: **Save** (runs
  Done), **Discard** (runs cancel), or **Stay**.
