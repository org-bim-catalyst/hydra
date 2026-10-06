# Feature Specification: Voids and Drawing Shapes in the Outline Editor

**Feature Branch**: `081-outline-voids-and-shapes`

**Created**: 2026-10-06

**Status**: Draft

**Input**: User description: "Voids and more drawing shapes in the site outline editor (follow-on to specs/079-site-boundary-manual-editing). Allow a ring of the outline to have voids inside it (a mall or hospital atrium under a skylight or open to the air), even when the map's building footprint doesn't show it. A cut drawn entirely inside a ring makes a void instead of being refused. A void only reduces the outline's area; it doesn't change which buildings belong to the site. Voids are saved, survive reopening the chat, reset like the rest of the outline, are editable and removable, are shown as holes, and Lucy knows about them. Splits stay allowed: a cut that splits a ring gives separate parts, switched between with [ and ]. New drawing shapes alongside the circle: rectangle, square and free polygon, each able to add to the outline or cut out of it."

## Context

Spec 079 lets a user correct a site's outline by hand. It deliberately refused any cut that would leave a hole inside the outline. That rule came from a misunderstanding: the user's concern was an outline breaking into pieces, not a hole inside a piece. In practice many malls and hospitals have an open atrium or courtyard, under a skylight or open to the sky. The map's building footprint usually doesn't show it, but later analyses need it. This feature lets those voids be drawn and kept, and adds the rectangle, square and free polygon to the circle as drawing shapes.

Vocabulary used below:

- **Part**: one connected piece of the outline (a "ring" in spec 079). An outline can have several parts, and the user moves between them with [ and ].
- **Void**: an area inside a part that is not part of the site, such as an atrium or courtyard.

## Clarifications

### Session 2026-10-06

- Q: Does a void only reduce the area, or must analyses also treat it as outside the site? → A: **It only reduces the area.** Voids are kept so they can be presented for accurate analysis later. Building membership is unchanged: a building belongs to the site when its outline is drawn within the same group of outlines (spec 077).
- Q: Should a cut that splits a part into disconnected pieces be refused? → A: **No.** The pieces become separate parts, switched between with [ and ], as today.
- Q: Which drawing shapes? → A: **Rectangle, square and free polygon**, alongside the existing circle.
- (Added before implementation) The user asked for the edit ribbon's tools to be **grouped Photoshop-style**: tools
  with similar functions share one ribbon button, whose vertical sub-menu opens from the ribbon. Choosing a
  tool there makes that button show the chosen tool's icon, and the button stays highlighted while the tool is
  in use. Examples given: add circle, cut circle and "convert ring to circle" in one group; add corner and
  delete corner in another (User Story 6).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cut an atrium out of a building (Priority: P1)

A user editing a mall's outline knows the mall has an open atrium that the map doesn't show. They choose a cutting shape, draw it inside the outline over the atrium, and the atrium becomes a void. The outline shows a hole there, the area drops by the void's area, and after Done the void is saved with the outline.

**Why this priority**: It is the reason for the feature. Without it, the outline of a building with an atrium is wrong, and the user cannot correct it.

**Independent Test**: Open the editor on a site, choose Cut with the circle, draw a circle fully inside the outline, and press Done. The outline shows a hole, the area is smaller by about the circle's area, and the hole is still there after the chat is reopened.

**Acceptance Scenarios**:

1. **Given** the editor is open, **When** the user cuts a shape fully inside a part, **Then** that area becomes a void: the part shows a hole there and the shown area drops by the void's area.
2. **Given** a cut that crosses a part's edge, **When** it is applied, **Then** it takes a bite out of the part's edge, as today, and no void is made.
3. **Given** a part with a void, **When** the user presses Done, **Then** the outline is saved with its void, and reopening the chat shows the void.
4. **Given** a cut fully inside an existing void, **When** it is applied, **Then** nothing changes and the user is told the area is already outside the site.
5. **Given** a cut that would leave nothing of a part, **When** it is applied, **Then** it is refused, as today.

---

### User Story 2 - Draw rectangles, squares and free polygons (Priority: P1)

Atriums, courtyards and wings are rarely circles. The user picks a rectangle, a square or a free polygon, chooses whether it adds to the outline or cuts out of it, and draws it on the map. It is applied exactly like the circle: added shapes join or extend the outline, cut shapes take away from it, and a cut fully inside a part makes a void.

**Why this priority**: Without these shapes, voids are only possible as circles, which rarely match a real atrium.

**Independent Test**: For each new shape, draw one Add and one Cut on a site and check that the outline changes as described. The rectangle and square follow the drag. The free polygon closes when the user finishes it.

**Acceptance Scenarios**:

1. **Given** the Rectangle tool, **When** the user presses at one corner and drags to the opposite corner, **Then** a rectangle following the pointer is previewed, and releasing applies it.
2. **Given** the Square tool, **When** the user drags, **Then** the preview keeps all four sides equal, and releasing applies it.
3. **Given** the Free polygon tool, **When** the user clicks corner after corner and then finishes (double-click, a click on the first corner, or Enter), **Then** the polygon is applied; Escape cancels it, and Backspace removes the last corner placed.
4. **Given** a free polygon that crosses itself, or has fewer than 3 corners, **When** the user finishes it, **Then** it is refused with an explanation and nothing changes.
5. **Given** a shape drawn with Add that touches no part, **When** it is applied, **Then** it becomes a new part, as the circle does today.
6. **Given** any new shape, **When** it is applied, **Then** it is one undo step, and Undo restores the outline exactly as it was.
7. **Given** a shape too small to mean anything (under the circle's existing minimum), **When** it is released, **Then** it is treated as a click and nothing is applied.
8. **Given** the map is drawn at an angle, **When** the user draws a rectangle or square, **Then** its sides follow the screen's axes as drawn. The editor already shows the map flat and north-up while editing, so the sides run north-south and east-west.

---

### User Story 3 - Adjust or remove a void (Priority: P2)

After cutting an atrium, the user finds it slightly off. They select the void and drag its corners, add or delete corners, use the Select tool on its corners, and undo or redo, exactly as for a part's outer edge. If the void was a mistake, they remove it and the part becomes solid there again.

**Why this priority**: A void that can't be adjusted has to be undone and redrawn, but the feature is still usable without this, so it comes second.

**Independent Test**: Make a void, then move one of its corners, add one, delete one, undo and redo each step, and finally remove the void. Each step changes the shown area and is saved with Done.

**Acceptance Scenarios**:

1. **Given** a part with a void, **When** the user moves, adds or deletes a void corner, **Then** the void changes, the area updates, and the change is one undo step.
2. **Given** a void edit that would make the void cross itself, cross the part's outer edge, or touch another void, **When** it is dropped, **Then** it is refused and put back, with an explanation, as outer-edge edits are today.
3. **Given** a void with 3 corners, **When** the user deletes a corner, **Then** it is refused, because a void needs at least 3 corners. To get rid of the void, they use Remove void.
4. **Given** a selected void, **When** the user chooses Remove void, **Then** the hole is filled, the area grows back, and the step can be undone.
5. **Given** the keyboard, **When** the user moves between corners, **Then** the corners of the active part's voids can be reached and moved as the outer corners can.
6. **Given** the Select tool, **When** a box covers corners of the outer edge and of a void, **Then** all of them are selected, and moving or deleting applies to each where it is valid.

---

### User Story 4 - Splits become separate parts (Priority: P2)

A cut that runs right through a part splits it into pieces. Each piece becomes its own part, and the user moves between parts with [ and ], as today. A part's voids stay with the piece they fall in.

**Why this priority**: Splitting already behaves this way for the circle. This story keeps that behaviour and extends it to the new shapes and to voids.

**Independent Test**: Draw a rectangle cut across a part, so that it splits in two. Two parts result, [ and ] move between them, and Done saves both.

**Acceptance Scenarios**:

1. **Given** a cut that divides a part, **When** it is applied, **Then** each piece becomes a separate part, and [ and ] move between them.
2. **Given** a part with a void, **When** a cut splits the part, **Then** each void stays with the piece it lies in. A void cut through by the split stops being a void, because it now opens to the outside.

---

### User Story 5 - Lucy knows about voids (Priority: P2)

When the user asks Lucy about the site after saving, Lucy's answers use the corrected area, which excludes the voids, and Lucy can say the outline has voids and how much area they remove.

**Why this priority**: The area and Lucy's narration should agree, but the corrected area already reaches Lucy through spec 079, so only the void details are new.

**Independent Test**: Save an outline with a void and ask Lucy for the site's area. The answer matches the area shown in the editor, and Lucy mentions the void when asked about the outline.

**Acceptance Scenarios**:

1. **Given** a saved outline with voids, **When** Lucy reports the site's area, **Then** it is the area excluding voids, the same figure the editor shows.
2. **Given** a saved outline with voids, **When** the user asks about the outline, **Then** Lucy can say how many voids there are and their total area.
3. **Given** an outline with voids, **When** Lucy decides which buildings belong to the site, **Then** the voids make no difference: membership is decided exactly as in spec 077.

---

### User Story 6 - Grouped tools in the edit ribbon (Priority: P2)

The edit ribbon has grown to more than fifteen buttons, and this feature adds eight more. Tools with similar
functions are grouped, as in Photoshop's tool bar: one ribbon button stands for a group and shows the tool
last chosen from it, with a small corner mark saying more tools are inside. Opening the group shows its tools
in a vertical sub-menu that comes out of the ribbon. Choosing one activates it, and the group button takes that
tool's icon and stays highlighted while it is in use.

**Why this priority**: Without it the new tools still work, but the ribbon becomes too long to scan and no
longer fits on smaller screens.

**Independent Test**: Open the Circle group, choose Cut circle, and check that the group button now shows the
Cut circle icon, is highlighted, and that a drag on the map cuts a circle. Then choose Corners → Delete corner
and check that the Circle button is no longer highlighted but still shows Cut circle.

**Acceptance Scenarios**:

1. **Given** the edit ribbon, **When** it is shown, **Then** it holds these groups, in this order, followed by
   the single buttons:
   - **Edit**: Edit corners, Select corners.
   - **Corners**: Add corner, Delete corner, Round corner, Remove void.
   - **Curves**: Curve edge, Draw arc.
   - **Circle**: Add circle, Cut circle, Make part a circle.
   - **Rectangle**: Add rectangle, Cut rectangle, Add square, Cut square.
   - **Polygon**: Add polygon, Cut polygon.
   - Single buttons: Undo, Redo, Cancel, Reset, Done.
2. **Given** a group button, **When** the user clicks it while its shown tool is not in use, **Then** that tool
   is activated straight away, with no menu.
3. **Given** a group button, **When** the user clicks it while its tool is already in use, presses and holds
   it, right-clicks it, or clicks its corner mark, **Then** the group's vertical sub-menu opens from the
   ribbon, listing each tool with its icon and name.
4. **Given** the sub-menu is open, **When** the user chooses a tool, **Then** the menu closes, the tool is
   activated, the group button shows that tool's icon and is highlighted, and every other group's highlight
   goes off.
5. **Given** a one-off action in a group (Make part a circle, Round corner, Remove void, Delete corner),
   **When** it is chosen, **Then** it runs (or opens its dialog), and the group button shows it so it can be
   repeated with one click.
6. **Given** a tool that can't be used right now (for example Remove void with no void selected), **When** the
   sub-menu is open, **Then** it is shown disabled, with the reason, as disabled buttons are today.
7. **Given** the user reopens the editor later, **When** the ribbon appears, **Then** each group shows the tool
   last chosen from it.
8. **Given** the keyboard, **When** focus is on a group button, **Then** Enter or Space activates its shown
   tool, Down arrow opens the sub-menu, the arrow keys move within it, Enter chooses, and Escape closes it and
   returns focus to the group button.
9. **Given** a small screen, **When** a sub-menu opens, **Then** it stays fully on screen, opening upwards if
   there is no room below.

---

### Edge Cases

- **A cut that covers a whole void and more**: the void merges into the cut. If the result still lies fully inside the part, it is one larger void; if it reaches the edge, it is a bite.
- **Two voids that touch or overlap**: they merge into one void.
- **An Add drawn over a void**: the covered part of the void is filled. If the Add covers the whole void, the void disappears.
- **A void reaching the outer edge after editing**: an edit that would make a void touch or cross the outer edge is refused. To turn a void into a bite, the user removes it and cuts again from the edge.
- **Reset**: Reset returns to the automatically found outline, which has no voids, so voids go with it, as all hand edits do today.
- **Outlines saved before this feature**: they have no voids, and they open, save and show exactly as before.
- **A building choice (spec 077) changed after voids were drawn**: voids are kept in the parts that remain. A void inside a building part that the choice removes goes with that part.
- **Very small voids**: a cut leaving a sliver under the existing minimum area is dropped, as today.
- **Many voids**: the existing limits on corners per outline still apply and count void corners. A save over the limit is refused with the existing message.
- **Keyboard and touch**: every new shape can be drawn without a mouse. The rectangle and square can be entered as typed sizes, as "Make ring a circle" does today. The free polygon can be placed by taps.

## Requirements *(mandatory)*

### Functional Requirements

**Voids**

- **FR-001**: The editor MUST let any part of the outline contain voids. A void is an area inside the part that is not counted as site.
- **FR-002**: A cut drawn fully inside a part MUST make a void, instead of being refused as spec 079 does today.
- **FR-003**: A void MUST lie entirely inside its part. It MUST NOT touch or cross the part's outer edge or another void. Overlapping or touching voids MUST be merged into one.
- **FR-004**: The outline's shown and saved area MUST exclude every void.
- **FR-005**: Voids MUST NOT change which buildings belong to the site. Membership MUST stay as defined in spec 077.
- **FR-006**: Voids MUST be saved with the outline correction, MUST be shown again when the chat is reopened, and MUST apply in every chat about the same site, like the rest of the correction.
- **FR-007**: Voids MUST be drawn on the map as holes in their part, in both the editor and the normal outline display.
- **FR-008**: A void's corners MUST be editable exactly as the outer corners are: move, add, delete, the Select tool, the keyboard, undo and redo. The same validity checks MUST apply.
- **FR-009**: The user MUST be able to remove a void in one action, filling its area back in, as one undo step.
- **FR-010**: Reset MUST discard voids together with every other hand edit.
- **FR-011**: Outlines saved before this feature MUST keep working unchanged.
- **FR-012**: Lucy MUST know when the saved outline has voids, including how many and their total area, and MUST report the site's area excluding them.

**Splits**

- **FR-013**: A cut that divides a part MUST produce separate parts, as today, reachable with [ and ]. Voids MUST stay with the piece they lie in. A void cut open by the split MUST stop being a void.

**Drawing shapes**

- **FR-014**: The editor MUST offer Rectangle, Square and Free polygon tools alongside Circle. Each MUST work in Add or Cut mode.
- **FR-015**: The Rectangle MUST be drawn by pressing at one corner and dragging to the opposite corner. The Square MUST be drawn the same way, with equal sides kept. Both MUST show a live preview that follows the pointer.
- **FR-016**: The Free polygon MUST be drawn by placing corners one at a time, with a live preview of the next edge. It MUST be finished by double-click, by a click on the first corner, or by Enter. Escape MUST cancel it, and Backspace MUST remove the last corner placed.
- **FR-017**: A free polygon with fewer than 3 corners, or one that crosses itself, MUST be refused when finished, with an explanation, and nothing applied.
- **FR-018**: Every shape MUST be applied the same way as the circle: Add joins or extends the outline, or makes a new part when it touches none; Cut takes a bite or makes a void. The result MUST be exactly what Done will save.
- **FR-019**: Every applied shape MUST be one undo step.
- **FR-020**: Every shape MUST be possible without a mouse. Rectangle and square MUST accept typed sizes. The free polygon MUST accept corners placed by tapping, or from the keyboard.
- **FR-021**: The tools MUST explain what they do while active, as the existing tools' hint lines do.

**Grouped ribbon**

- **FR-022**: The edit ribbon MUST group related tools behind one button per group (the groups in User Story 6),
  each showing the tool last chosen from it and a corner mark indicating it holds more tools.
- **FR-023**: A group's tools MUST open in a vertical sub-menu emerging from the ribbon at that button. It
  opens by clicking the button while its tool is active, by press-and-hold, by right-click, or by its corner
  mark.
- **FR-024**: Choosing a tool from a sub-menu MUST activate it, set the group button's icon to that tool, and
  keep the button highlighted while the tool is in use. Only the group holding the active tool is highlighted.
- **FR-025**: Each group's last-chosen tool MUST be remembered for the user on that browser, across editor
  sessions.
- **FR-026**: Groups and sub-menus MUST be fully usable by keyboard (Enter/Space, Down arrow to open, arrow
  keys, Escape) and by touch (press-and-hold), MUST name every tool for assistive technology, and MUST show
  disabled tools with their reason.
- **FR-027**: The grouping MUST be a reusable part of the workspace ribbon, not specific to the outline editor,
  so other ribbons (Layers, Analysis) can group their buttons the same way later.

### Key Entities

- **Outline correction** (from spec 079): the user's hand-corrected outline for a site. Each part now holds its outer edge and zero or more voids, and the stored area excludes the voids.
- **Part**: one connected piece of the outline, with one outer edge and any number of voids.
- **Void**: an area inside a part that is not site, such as an atrium or courtyard. It is described by its own corners, lies fully inside its part, and touches nothing else.
- **Drawn shape**: a temporary shape (circle, rectangle, square or free polygon) with an Add or Cut mode, which the editor applies to the outline and then discards.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can turn a mall's atrium into a void, and save it, in under 1 minute.
- **SC-002**: The area shown after cutting a void differs from the outer area minus the void's own area by less than 0.5%.
- **SC-003**: 100% of saved voids are still present, with the same corners, after the chat is reopened and in other chats about the same site.
- **SC-004**: Building membership is identical with and without voids, for every site tested.
- **SC-005**: Every new shape can be drawn, applied and undone with the mouse, and with the keyboard or touch alone.
- **SC-006**: 100% of outlines saved before this feature open and save unchanged.
- **SC-007**: Lucy's reported site area matches the editor's area, with voids excluded, every time.
- **SC-008**: The edit ribbon shows 11 buttons (6 groups plus 5 single actions) instead of 23, and any tool is
  reachable in at most two clicks.

## Assumptions

- Voids are a property of the user's hand correction. The automatic outline lookup still ignores holes in map data (spec 042), and nothing here changes it.
- "Square" means a square drawn as the rectangle is, but with equal sides. Rotated rectangles and squares are not part of this feature: the editor already works on a flat, north-up map, so drawn sides run north-south and east-west. Their corners can be moved afterwards like any others.
- Google's own drawing tools are not used. Google deprecated them in August 2025 and removed them from the Maps version released in May 2026. The shapes are drawn by the editor's own tools, as the circle is.
- Using voids in future analyses (solar, shadows) belongs to those analyses' specs. This feature only keeps voids accurate and available.
- The existing size limits (parts per outline, corners per part and in total) apply to voids too; void corners count toward the totals.
- The existing per-user, per-site ownership and audit rules for corrections apply unchanged.
