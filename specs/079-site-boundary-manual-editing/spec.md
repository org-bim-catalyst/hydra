# Feature Specification: Hand-Edit the Site Outline

**Feature Branch**: `079-site-boundary-manual-editing`

**Created**: 2026-09-27

**Status**: Draft (clarified 2026-09-27)

**Input**: User description: "Letting you edit the outline's corner points by hand". Manual site
boundary editing: the user corrects the site outline drawn in the Studio map by hand. They can
drag a corner point to move it, add a corner point on an edge, and delete a corner point. This
works on the site's own outline and on each separate building outline (specs/077 members, e.g.
Muscat Grand Mall - Phase 2). The edited outline is saved, survives reopening the chat, is marked
as user-corrected, and its new area is shown. Lucy knows it was hand-edited, so later turns use
the corrected shape. An edit can be undone or cancelled, and the outline can be reset to the one
Lucy found. Follow-on to specs/042-site-boundary-resolution, which deliberately deferred this,
and specs/077-site-boundary-membership.

Refined by the user on 2026-09-27: "after Lucy finishes the outline either with one building or
more, it asks first if the user would like to edit the boundaries since the confidence is not
100%. If the user accepts, you remember the camera mode (3D or 2D, rotating or fixed), then Lucy
switches to edit mode, where the user can make use of the editing tool, and when [they finish,
they exit] the edit mode. Lucy applies the new outline with effects and restores the view to the
same condition."

**Terms used below**

- **Outline**: everything highlighted for the chat's site. That is one or more **rings**: the
  site's own ring, plus a ring for each included building that stands apart (specs/077).
- **Corner point**: a vertex of a ring.
- **Found outline**: the outline Lucy produced by resolving the site and applying the building
  choice (specs/042, specs/077).
- **Hand-edited outline**: an outline the user has corrected and saved.
- **Edit mode**: a dedicated map mode in which the outline's corners can be changed. The user
  enters it and leaves it through Done or Cancel.
- **View state**: how the map was showing the site before edit mode: 3D or plan view, rotating
  or fixed, heading, zoom and position.

## Clarifications

### Session 2026-09-27

- Q: Does a hand edit apply only in its chat? → A: **In all of the user's chats.** Showing the
  same site in any of the user's chats uses their edited outline. Each chat can still reset it
  (FR-023, FR-024).
- Q: The outline is hand-edited, then a different building choice is picked. What happens? →
  A: **The edits are kept.** The choice adds or removes whole buildings around the edited shape
  instead of rebuilding it (FR-022).
- Q: How does editing start? → A: **Lucy offers it** once the outline is final (after the
  building choice, when there is one), because the outline is never certain. Accepting it
  remembers the view state, switches to edit mode, and restores the view state on exit. On
  exit, the new outline is drawn with its usual animated effects (User Story 1).
- Plan review (2026-09-27, approved): the edit choice leads the turn's analysis suggestions
  instead of replacing them; "Keep the outline as it is" in the building question runs a short
  turn so the edit offer can follow; the area may update on drop rather than mid-drag if the map
  only reports corner moves on drop (research D1, D6).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Lucy offers an edit, and the view comes back afterwards (Priority: P1)

"Show me Muscat Grand Mall". Lucy outlines the mall and asks which buildings it includes. The
user picks B. Once the outline is final, Lucy asks whether they want to adjust it. The user says
yes. The map was in 3D view and rotating. It stops, turns to a top-down plan view facing north,
and frames the whole outline. Corner handles appear with Done, Cancel and Undo controls. The user
drags one corner of Phase 2 onto the building's edge and presses Done. Lucy draws the corrected
outline with its animated border. The map returns to 3D view, rotating, at the zoom it had before.
Lucy then says the new area.

**Why this priority**: This is the flow the user asked for. Moving existing corners fixes most
near-miss outlines on its own, the reason this was deferred from specs/042. It is the smallest
useful slice.

**Independent Test**: Resolve any site with the map in 3D view and rotating. Accept the edit
offer, move one corner, press Done. The outline keeps the moved corner, the area changes, and the
map is back in 3D view and rotating at the same zoom and position.

**Acceptance Scenarios**:

1. **Given** Lucy has finished an outline (with or without a building choice), **When** she
   ends her turn, **Then** she offers to adjust the outline, saying how certain she is in plain
   words. "Edit the outline" is the first choice, followed by any analysis suggestions for the
   turn, and "It looks right" last. When a building choice is being asked, the edit offer waits
   until that choice is made or kept. Keeping the outline as it is ("Keep the outline as it is")
   runs a short turn so the edit offer can follow it.
2. **Given** the edit offer, **When** the user accepts it (by picking it or by saying so),
   **Then** the view state is remembered, rotation stops, the map turns to a top-down plan view
   facing north, the whole outline is framed, and every corner of every ring shows a handle.
3. **Given** edit mode, **When** the user drags a corner, **Then** the ring follows the pointer
   and the shown area updates, while dragging if the map reports corner moves continuously,
   otherwise when the corner is dropped.
4. **Given** edit mode with changes, **When** the user presses Done, **Then** the outline is
   saved, drawn with its animated border (the same effect a newly found outline gets), marked
   as hand-edited with its new area, and the remembered view state is restored.
5. **Given** edit mode, **When** the user presses Cancel, **Then** the outline returns exactly
   to its shape before edit mode, nothing is saved, and the view state is restored.
6. **Given** edit mode, **When** the user presses Undo, **Then** the last change (a move, add or
   delete) is reverted. Repeated Undo walks back to the shape edit mode started with.
7. **Given** the edit offer, **When** the user says it looks right or ignores it, **Then** nothing
   changes. The user can still start editing later from a control on the map or by asking Lucy.
8. **Given** edit mode, **Then** the 3D/plan and rotation controls are disabled, and panning and
   zooming still work.

---

### User Story 2 - Add and remove corners (Priority: P1)

The found outline has too few corners to follow an L-shaped building, or has a stray spike. In
edit mode the user adds a corner in the middle of an edge and drags it out, or deletes the
spike's corner.

**Why this priority**: Moving corners alone cannot fix a missing wing or a spike, so without
add and delete many real corrections are impossible.

**Independent Test**: In edit mode, add a corner on an edge, drag it, delete another corner, and
press Done. The saved ring has one corner more and one corner fewer than before, in the positions
chosen.

**Acceptance Scenarios**:

1. **Given** edit mode, **When** the user drags the midpoint handle shown on every edge,
   **Then** a new corner is inserted there and follows the pointer.
2. **Given** edit mode and a ring with more than 3 corners, **When** the user deletes a corner
   (from the corner's menu, or by selecting it and pressing Delete), **Then** the ring closes
   across the gap and the area updates.
3. **Given** a ring with exactly 3 corners, **When** the user tries to delete one, **Then** the
   deletion is refused with a short explanation that an outline needs at least 3 corners.
4. **Given** edit mode, **When** a move, add or delete would make a ring cross itself, **Then**
   the change is refused (the corner snaps back, or the delete is refused) and the user is told
   why.

---

### User Story 3 - The correction sticks, in every chat, and Lucy knows about it (Priority: P1)

After Done, the user keeps talking to Lucy, reopens the chat later, or asks about the same site
in a new chat. The hand-edited outline is what appears everywhere. Lucy treats it as the site:
she reports its area, and analyses use it.

**Why this priority**: An edit that is lost on reload, overwritten by the next turn, or ignored
by the next chat is worse than no edit. The user chose that edits apply in all their chats.

**Independent Test**: Save a hand-edited outline. Reload the page and reopen the chat: the same
outline shows, marked hand-edited. Ask "How big is the site?": Lucy gives the edited area. Start
a new chat and ask for the same site: the edited outline appears, and Lucy says it is the user's
corrected outline.

**Acceptance Scenarios**:

1. **Given** a saved hand-edited outline, **When** the user reopens the chat (after a reload, a
   sign-out and sign-in, or on another device), **Then** the same rings with the same corners are
   shown, marked hand-edited.
2. **Given** a saved hand-edited outline, **When** the user sends their next message, **Then**
   Lucy knows the outline was edited by hand and its current area, and does not describe the
   found outline as current.
3. **Given** a saved hand-edited outline, **When** the user asks to see the same site again in
   that chat, **Then** the camera flies to it and the hand-edited outline stays.
4. **Given** the user has a hand-edited outline for a site, **When** they ask for the same site
   in any other chat of theirs, **Then** that chat shows their hand-edited outline instead of a
   freshly found one. Lucy says she is using their corrected outline and offers to reset it. The
   building question and the edit offer are not repeated for that site.
5. **Given** a saved hand-edited outline, **When** the user runs a site analysis (e.g. Analyze
   Site or Solar Analysis) or anything else that uses the site's extent, **Then** it uses the
   hand-edited rings. This includes the solar dome and the reach of the surrounding-building
   fetch (specs/076).
6. **Given** the user presses Done, **Then** the chat shows a short line recording it (for
   example "You edited the outline — now 35,210 m²"), and that line is still there when the chat
   is reopened.
7. **Given** a user's hand-edited outline, **Then** no other user ever sees or receives it.

---

### User Story 4 - Change the building choice without losing the edits (Priority: P2)

The user has hand-edited the mall's outline and then asks Lucy to add Phase 2, or picks a
different building choice. Phase 2 is added around the edited shape. Their corrections to the
mall's ring stay.

**Why this priority**: The user chose that edits are kept. It matters only once there are edits
and a building choice to change, hence P2.

**Independent Test**: Edit the mall's ring, then choose "with its nearby buildings". The mall's
ring keeps its hand-placed corners and a Phase 2 ring appears. Remove Phase 2 again: only the
Phase 2 ring disappears.

**Acceptance Scenarios**:

1. **Given** a hand-edited outline, **When** a building that stands apart is added, **Then** its
   ring is added next to the edited rings, and the edited rings are unchanged.
2. **Given** a hand-edited outline, **When** a building that shares a wall with an edited ring is
   added, **Then** that building's footprint is joined onto the edited ring. The ring's hand-placed
   corners away from the join stay where they are.
3. **Given** a hand-edited outline, **When** a building is removed, **Then** only that building's
   ground is taken out: a separate ring disappears, or a joined footprint is cut back out of the
   edited ring. The rest of the edits stay.
4. **Given** any of the above, **Then** the outline stays marked hand-edited, the area and chat
   line update, and the building question's letters describe the choice as today (specs/077).

---

### User Story 5 - Go back to what Lucy found (Priority: P2)

The user has made a mess of the edits, or the site has changed, and wants Lucy's own outline
back.

**Why this priority**: A safety net. Without it, a bad saved edit can only be fixed by editing
again. It is not needed for the first useful release, hence P2.

**Independent Test**: Save a hand-edited outline, choose "Reset to Lucy's outline" and confirm.
The found outline, with the building choice in force, is shown again without the hand-edited
mark.

**Acceptance Scenarios**:

1. **Given** a hand-edited outline, **When** the user chooses "Reset to Lucy's outline" on the
   map and confirms, or asks Lucy to reset it, **Then** the found outline is shown again, drawn
   with its animated border, with its area and without the hand-edited mark. A chat line records
   the reset.
2. **Given** the reset, **Then** the user's saved correction for that site is removed, so other
   chats asking for that site get Lucy's outline again.
3. **Given** an outline that was never hand-edited, **Then** no reset option is offered.

---

### User Story 6 - Edit without a mouse (Priority: P3)

A keyboard or touch user can do everything a mouse user can.

**Why this priority**: Required by the platform's accessibility principle (WCAG 2.1 AA,
keyboard operability). It can ship after the pointer interactions are proven.

**Independent Test**: With only the keyboard, accept the offer, select a corner, nudge it, add a
corner, delete a corner, undo, and press Done. Repeat on a touch screen.

**Acceptance Scenarios**:

1. **Given** edit mode, **When** the user tabs through the corners, **Then** the selected corner
   has a visible focus state and is announced (e.g. "Corner 3 of 12, Muscat Grand Mall").
2. **Given** a selected corner, **When** the user presses the arrow keys, **Then** it moves in
   small ground steps north, south, east or west (the map faces north in edit mode), with a larger
   step when a modifier key is held. Delete removes the corner, and a key inserts a new corner
   after it. Standard undo shortcuts undo, and Escape cancels.
3. **Given** a touch screen, **When** the user drags a handle, **Then** the corner moves and the
   map does not pan at the same time. Handles are large enough to hit with a finger.

---

### Edge Cases

- **A different site arrives during edit mode.** Lucy resolves a different site in the same
  chat. Edit mode ends without saving, the view state is restored, and the user is told their
  unsaved changes were dropped.
- **Leaving during edit mode.** Leaving the Studio and coming back keeps edit mode and its
  changes, because the workspace keeps its state across navigation. Switching to another chat
  with unsaved changes asks whether to save or discard.
- **The user moves the map during edit mode.** Panning and zooming are allowed. On exit the
  view state from *before* edit mode is restored, not the panned one.
- **The camera mode was changed elsewhere.** The view state is captured once, on entry, and
  restored on exit even if something else tried to change the camera mode meanwhile. Those
  controls are disabled during edit mode anyway.
- **Reduced motion.** When the user's system asks for reduced motion, the switch into and out of
  edit mode happens without animated camera moves. Rotation is not resumed on exit unless it was
  running before.
- **Saving fails** (offline, server error, or the chat was changed elsewhere). The user sees an
  error and edit mode stays open with their changes, so they can retry or cancel. Nothing is
  dropped silently.
- **Two tabs.** The same outline is edited in two tabs. The second save is refused with a notice
  that the outline changed elsewhere, and the latest saved outline is offered to load.
- **Zoom.** Handles keep a constant on-screen size at every zoom level. Where corners overlap on
  screen, the user zooms in to pick the one they want.
- **Very large rings.** A ring with hundreds of corners (a traced park, specs/042) stays usable
  while dragging, with no visible lag.
- **Degenerate results.** An edit that would leave a ring with near-zero area, or with two
  corners on the same spot, is refused like a self-crossing edit.
- **Rings overlapping each other.** A ring dragged over another ring of the same site is allowed.
  The saved area counts overlapping ground once.
- **Edits that drift from the site.** An edited ring that no longer overlaps the found outline
  at all, or that grows far beyond it, is refused on save with an explanation. This stops a
  correction from turning into a different site.
- **Same name, different place.** A saved correction is reused only for the same site in the
  same place, never for another site that shares its name elsewhere.
- **Chat with no outline.** No edit offer and no edit control.
- **A shared chat.** Only the chat's owner can edit its outline. Anyone else who can view the
  chat sees the outline without the edit offer or control.

## Requirements *(mandatory)*

### Functional Requirements

**Offering and entering edit mode**

- **FR-001**: When Lucy finishes an outline, and any building choice for it has been made or
  kept, she MUST offer to edit it, stating how certain she is in plain words. The edit choice
  MUST come first, ahead of any analysis suggestions shown with it. It MUST NOT be offered when the outline shown is already
  the user's hand-edited one.
- **FR-002**: Users MUST also be able to enter edit mode at any time from a control next to the
  outline on the map, or by asking Lucy.
- **FR-003**: On entering edit mode, the system MUST remember the view state (3D or plan view,
  rotating or fixed, heading, zoom, position). It MUST then stop rotation, switch to a top-down
  plan view facing north, and frame the whole outline.
- **FR-004**: On leaving edit mode, by Done or Cancel, the system MUST restore the remembered
  view state exactly, including resuming rotation only if it was running.
- **FR-005**: During edit mode, the 3D/plan and rotation controls MUST be disabled. Panning and
  zooming MUST remain available. The surrounding 3D buildings MAY be faded so the ground and
  corners stay visible.

**Editing**

- **FR-006**: In edit mode, the system MUST show a handle on every corner of every ring (the
  site's own ring and each separate building ring) and a midpoint handle on every edge.
- **FR-007**: Users MUST be able to move a corner by dragging its handle. The ring MUST redraw
  continuously during the drag. A drag that starts on a handle MUST move the corner, not the
  map.
- **FR-008**: Users MUST be able to add a corner by dragging an edge's midpoint handle.
- **FR-009**: Users MUST be able to delete a corner. The system MUST refuse when the ring would
  drop below 3 corners.
- **FR-010**: The system MUST refuse any change that would make a ring cross itself, collapse to
  near-zero area, or put two corners on the same spot, and MUST tell the user why.
- **FR-011**: The system MUST show the outline's area during edit mode, updated with every
  change.
- **FR-012**: Users MUST be able to undo each change in reverse order, back to the shape edit
  mode started with. Redo MAY be offered.
- **FR-013**: Cancel MUST restore the exact shape from before edit mode and save nothing.
- **FR-014**: Every editing action MUST be possible with a keyboard alone and on a touch screen
  (User Story 6), meeting WCAG 2.1 AA.

**Done: applying and saving**

- **FR-015**: Done MUST save the edited rings. The outline MUST then be drawn with its animated
  border, the same effect a newly found outline gets, and marked as hand-edited. Its source is
  recorded as user-corrected.
- **FR-016**: The saved area MUST be worked out by the server and MUST count ground covered by
  more than one ring once. Lucy then states the new area in the chat.
- **FR-017**: Each Done MUST add a short, persisted line to the chat saying the outline was edited
  by hand and giving its new area.
- **FR-018**: A failed save MUST keep edit mode open with the user's changes and show an error.
  No save failure may be silent (constitution §2 VIII).
- **FR-019**: A save MUST be refused when the outline changed since edit mode began (for
  example, saved from another tab). The user MUST be offered the latest saved outline.
- **FR-020**: Only the chat's owner MAY save an edit. The server MUST reject anyone else. It MUST
  validate every ring: at least 3 corners, valid coordinates, not self-crossing, a bounded
  number of corners, and still overlapping the found outline without growing far beyond it.

**Lucy and the rest of the platform**

- **FR-021**: After a save, Lucy's later turns MUST know the outline is hand-edited and its
  current area. They MUST report that area, and MUST NOT describe the found outline as current.
- **FR-022**: Changing the building choice (specs/077) on a hand-edited outline MUST keep the
  edits. A building that stands apart adds or removes its own ring. A building that shares a wall
  is joined onto, or cut back out of, the edited ring it touches. Hand-placed corners away from
  that building stay where they are.
- **FR-023**: A saved hand edit MUST apply in all of the user's chats. Whenever the same user
  gets the same site (the same place, not just the same name), in any chat, the system MUST show
  their hand-edited outline instead of a freshly found one. Lucy MUST say it is their corrected
  outline and offer a reset. The building question and the edit offer MUST NOT be repeated for
  it.
- **FR-024**: Asking for the same site again in a chat with a hand-edited outline MUST keep it.
  Asking for a different site MUST replace the outline as it does today.
- **FR-025**: Everything that uses the site's extent MUST use the hand-edited rings: analyses,
  the solar dome, and the reach of the surrounding-building fetch.
- **FR-026**: A user's hand-edited outlines MUST never be shown or given to any other user.

**Reset**

- **FR-027**: Users MUST be able to reset a hand-edited outline to the found outline, both from
  a control on the map (after confirming) and by asking Lucy. A reset MUST redraw the found
  outline with its animated border, clear the hand-edited mark, remove the user's saved
  correction for that site, and add a chat line recording the reset.
- **FR-028**: The reset control MUST NOT be offered for an outline that was never hand-edited.

**Edit mode lifecycle**

- **FR-029**: Edit mode and its unsaved changes MUST survive leaving and returning to the Studio.
  Switching to another chat with unsaved changes MUST ask the user to save or discard.
- **FR-030**: If a different site's outline replaces the chat's outline during edit mode, edit
  mode MUST end without saving, the view state MUST be restored, and the user MUST be told that
  their unsaved changes were dropped.

### Key Entities *(include if feature involves data)*

- **Chat outline**: the outline currently in force for a chat. It holds its rings, area, site
  name, and source. "User-corrected" is a new source alongside the existing ones (map data,
  rendered map, AI interpretation, fallback).
- **Found outline snapshot**: the outline and building choice as Lucy produced them, kept when a
  hand edit is first saved. Reset restores it.
- **Site correction**: a user's hand-edited outline for one site, kept per user and reused in
  every chat of theirs that shows that site. It holds the site's identity (its name and place),
  the edited rings, the building choice they cover, and when it was saved. It is removed by a
  reset.
- **Outline edit record**: the persisted chat line for a save or a reset. It holds who, when and
  the resulting area. It is visible in the chat, and Lucy reads it on later turns.
- **View state** (client only, never saved to the server): the camera condition captured when edit
  mode begins and restored when it ends.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From accepting Lucy's offer, a user can correct one misplaced corner and press Done
  in under 30 seconds.
- **SC-002**: After leaving edit mode, the view matches the one before it in every checked
  respect: 3D or plan view, rotating or fixed, heading within 1°, zoom, and position. This holds
  for all four combinations of 3D/plan and rotating/fixed.
- **SC-003**: 100% of saved hand-edited outlines reappear with identical corners (within 0.1 m)
  after reopening the chat, in a new chat for the same site, and on another device.
- **SC-004**: Across the 7 Muscat test sites from specs/077 and BurJuman (Dubai), every edit action
  (move, add, delete, undo, cancel, done, reset) works on every ring, including a separate
  building ring such as Muscat Grand Mall - Phase 2.
- **SC-005**: While dragging a corner on a ring of 500 corners, the outline keeps up with the
  pointer with no visible lag on the reference development machine.
- **SC-006**: In 10 follow-up turns after a hand edit (area questions, "show me the same site",
  a building choice, analyses), Lucy never reports the found outline's area as current, never
  drops the hand edits without the user asking, and never repeats the edit offer for that site.
- **SC-007**: No saved outline ever has a self-crossing ring or one with fewer than 3 corners.
  This is checked both in the map and on the server.
- **SC-008**: Every editing action can be completed with the keyboard alone, and the automated
  accessibility checks pass for the edit controls.

## Assumptions

- **The offer is always made on a fresh outline.** Resolution never reaches 100% certainty in
  practice, so "offer when not fully certain" means every newly found outline gets the offer. The
  offer states the certainty in words, for example "I'm fairly sure about this outline" for
  medium.
- **Edit mode is always a north-up plan view.** Corners are placed on the ground and arrow keys
  move them north, south, east and west. Editing in a tilted or rotating 3D view is out of scope.
- **Site identity for reuse across chats** is the site's name together with its place (a small
  distance between the found outlines' centres), so two different places sharing a name never
  share a correction.
- **No snapping** to buildings, roads or other rings in this release. Corners are placed freely.
- **No adding or removing whole rings by hand.** Which buildings belong to the site stays a
  building choice (specs/077). Hand editing changes the shape of existing rings only.
- **No drawing a site from scratch** and no uploaded outline files. Editing needs a found outline
  to start from.
- **Editing by words** ("move the north corner 5 m east") is out of scope. Lucy can start edit
  mode, reset, and talk about an edited outline, but corners are moved in the map.
- **Confidence after a hand edit.** The user has confirmed the shape, so a hand-edited outline uses
  the high-confidence border style.
- **Clearing the outline when a new chat starts** is a separate deferred decision and is not
  changed here.
- **Depends on** specs/042-site-boundary-resolution (resolving and persisting the chat outline),
  specs/077-site-boundary-membership (multiple rings, building choices and the offer pattern),
  specs/076 (the building fetch that follows the outline's extent), and the Studio map's camera
  modes and rotation (3D/plan, rotate).
