# Research: Hand-Edit the Site Outline

**Feature**: specs/079-site-boundary-manual-editing | **Date**: 2026-09-27

There are no open NEEDS CLARIFICATION items. Each decision below records what was chosen, why,
and what was rejected. Two decisions change the spec slightly. They are marked **Spec
amendment** and are listed at the end.

---

## D1 — The editor: the map's own editable polygons, one per ring

**Decision**: In edit mode, hide the animated outline and draw each ring as its own
`google.maps.Polygon` with `editable: true, draggable: false, clickable: true`. This is the
native editing already in the Maps JS `maps` library. It needs no drawing library and no new
package.

The native polygon provides:

- Corner handles.
- Midpoint "ghost" handles on every edge, which insert a corner when dragged (FR-006, FR-008).
- Pointer and touch dragging that moves the corner, not the map (FR-007).
- Constant on-screen handle size at every zoom.

Our `editablePolygonController` listens to each path's `set_at`, `insert_at` and `remove_at`
events. It does these jobs:

- **Validates** each change with `ringGeometry.ts`: only the edges touching the changed corner
  are tested against the rest of the ring, which is O(n). It also checks for fewer than 3
  corners, near-zero area (< 1 m²) and duplicate corners (< 0.05 m apart). An invalid change is
  reverted on the path with a guard flag, so the revert doesn't re-enter, and the reason is
  shown (FR-010).
- **Pushes** each accepted change onto the session's undo stack as `{ring, op, index, before,
  after}` (FR-012).
- **Deletes** a corner from a context action. The polygon's `contextmenu` event and long-press
  both give `PolyMouseEvent.vertex`, which opens a small MUI menu. The Delete key works too
  (D10). Native polygons have no delete UI of their own (FR-009).
- **Recomputes** the running area as the sum of per-ring areas in a local east-north-up frame,
  using the same shoelace approach as the backend `GeometryMath.AreaSquareMeters`. The toolbar
  shows it as "about N m²". The server's number on Done is authoritative and counts overlap once
  (FR-011, FR-016).

Only one ring is editable at a time: the one the user last touched or tabbed into. The others
are drawn non-editable with their corners dimmed. This keeps 500-corner rings responsive, since
a traced park doesn't show 1,000 handles alongside every other ring. The user switches rings by
clicking one, or with the keyboard.

**Rationale**:

- It is the least code for the full interaction set.
- It is GPU-cheap: the polygons are 2D map overlays, not another WebGL layer. That respects the
  one-anchor, renderer-state-is-global constraints from the 049–052 plan, and the
  map-contention lesson from the sphere performance bug.
- Its touch handling is proven.
- One polygon per ring, not one polygon with several paths: overlapping rings in a multi-path
  polygon render as holes under the even-odd rule. The spec allows rings to overlap.

**Alternatives considered**:

- *AdvancedMarkerElement handles plus a plain polygon*: this gives full control and live
  mid-drag events, and markers are keyboard-focusable. But a 500-corner ring would need about
  1,000 DOM markers, and pointer capture, touch and handle sizing would all be ours to build.
  Kept as the fallback if the spike below fails.
- *Terra Draw or the deprecated `DrawingManager`*: `DrawingManager` is deprecated and draws new
  shapes rather than editing existing ones. Terra Draw is a new dependency for less than the
  native editor gives.
- *Editing inside the Three.js outline renderer*: this means ray-picking on a tilted 3D scene.
  Edit mode is plan view anyway, and it would add input handling to the renderer, which
  specs/049's constraints keep framework-owned.

**Spike (first implementation task)**: check whether `set_at` fires continuously while a corner
is dragged or only when it is dropped. The ring itself redraws continuously either way.

- **If it fires only on drop**: the area readout and the self-crossing check update when the
  corner is dropped. A crossing drop snaps the corner back, as US2 AS4 already allows.
- **If it fires continuously**: they update live.

**Spec amendment A** (only if the spike shows drop-time events): US1 AS3 changes from "the shown
area updates while dragging" to "…updates when the corner is dropped". FR-011 ("updated with
every change") holds either way.

---

## D2 — View state: capture once, enter a north-up plan view, restore exactly

**Decision**: `viewStateCapture.ts` works in three steps.

1. **On entry**, it reads `viewerEngineStore.camera` (`mode`, `rotationEnabled`) and the map's
   `center`, `zoom`, `heading` and `tilt`, and stores them on the edit session. The capture
   happens once, so the restore target never drifts (spec edge case).
2. **To enter**, it sets `camera.rotationEnabled = false` through `setCamera`, so the existing
   `RotationDriver` stops cleanly. It then sets `mode = 'plan'` (tilt 0 via
   `CAMERA_VIEW_MODE_TILT`) and heading 0, and calls `fitBounds` on the union of all rings'
   bounds with 48 px padding.
3. **On exit** by Done, Cancel, or a forced exit (FR-030), it:
   - restores `mode` through `setCamera`;
   - issues one `moveCamera({center, zoom, heading, tilt})`;
   - defends zoom and heading with the existing `cameraRestoreGuard`, as the Studio's
     navigation restore already does;
   - restores `rotationEnabled` last. The `RotationDriver` is then seeded with the restored
     heading, which the guard's `shouldEnforceHeading()` already handles.

If `prefers-reduced-motion` is set, both transitions use `moveCamera` instantly. Otherwise they
use the existing animated camera path.

While a session exists, the 3D/plan and rotation controls read `editSession !== null` and render
disabled with a tooltip, "Finish editing the outline first" (FR-005).

**Rationale**: It reuses the camera state that already exists in `viewerEngineStore` and the
guard that already solved "Maps JS fights a restore". SC-002 (heading within 1°) is met by the
guard's 1e-3 match.

**Alternatives considered**: Editing in the current tilted or rotating view was rejected in the
spec: ground corners are ambiguous under tilt, and the arrow keys need north up. A separate
second map for editing was also rejected. It would double GPU load, and it would lose the user's
context.

---

## D3 — Geometry: NetTopologySuite on the server, plain TypeScript on the client

**Decision**:

- **Server**: `ISiteRingGeometry` in Application, implemented by `NtsSiteRingGeometry` in
  Infrastructure. It works in a local metric frame, an equirectangular projection around the
  outline's centroid, the same frame `GeometryMath` uses. It offers:
  - `Validate(ring)`: `IsSimple`, `IsValid`, corner count, area > 1 m², no duplicate corners.
  - `UnionArea(rings)`: overlapping ground counted once (FR-016).
  - `Join(rings, footprint, gapMeters)` and `Cut(rings, footprint, gapMeters)` (D9).
- **Client**: `ringGeometry.ts`, about 150 lines, with an ENU projection, shoelace area, segment
  intersection and duplicate-corner checks. It is for immediate feedback only. The server
  re-validates everything (SC-007: checked in both places).

**Rationale**: Only exact vector operations preserve hand-placed corners through a building
choice (FR-022). NTS is the .NET port of JTS: pure managed code, BSD-3-licensed, and mature on
shared-wall and collinear edge cases. It stays in Infrastructure, so Domain and Application keep
the plain `GeoPoint` lists they already use.

**Alternatives considered**:

- *Reuse the 0.5 m raster union*: it re-traces and moves every corner. Rejected.
- *Handwritten clipping*: rejected. See Complexity Tracking in plan.md.
- *A JS geometry library on the client* (turf or jsts): the client needs only local checks and
  area, which don't justify a bundle-size increase.
- *SQL Server `geography` methods*: they would move geometry rules into the database and couple
  Application tests to SQL. Rejected.

---

## D4 — Revision token for stale saves

**Decision**:

- `UserChat.ActiveBoundary` gains `Revision` (Guid). It is regenerated on every change to the
  chat's outline: resolve, membership choice, reset and link.
- `SiteBoundaryCorrection` has its own `Revision` (Guid), regenerated on every save or
  membership change.
- The **effective revision** is the linked, live correction's revision if there is one,
  otherwise the chat's. It is returned in `ChatDetailDto.activeBoundary.revision`, captured by
  the client when edit mode begins, and sent as `expectedRevision` with a save or reset.
- If they differ, the handler throws `ConcurrencyConflictException` (Domain, already mapped to
  409), with a `currentRevision` extension. The client shows "The outline changed in another
  tab" and offers "Load latest", which refetches chat detail and restarts the session from it
  (FR-019).
- EF's `RowVersion` on both rows still catches the true race between the check and
  `SaveChanges`. It surfaces as `DbUpdateConcurrencyException`, which `ProblemDetailsMiddleware`
  already maps to 409, so no translation is needed. The client handles it like any conflict and
  reloads the chat.

**Rationale**: `UserChat.RowVersion` changes on any chat update (rename, pin, message append),
so using it would refuse saves falsely. A dedicated revision changes only when the outline does.

**Alternatives considered**:

- *Last write wins*: violates FR-019.
- *An ETag on the whole chat*: false conflicts, the same problem as `RowVersion`.

---

## D5 — Where a hand edit lives, and how other chats find it

**Decision**: A hand edit is a per-user **`SiteBoundaryCorrection`** aggregate. It is the single
source of truth for the edited rings. Each chat keeps its **found outline** in its existing
`ActiveBoundary` columns, never overwritten by edits, plus a nullable `CorrectionId`.

- **Outline in force** is computed by `EffectiveSiteBoundary`: the chat's found outline, with
  polygon, additional polygons, area and source replaced by the correction's when `CorrectionId`
  points at a live correction owned by the same user. This is
  `ActiveSiteBoundary.WithCorrection(...)`, a pure domain method.
  - Everything downstream receives the effective outline, unchanged in type:
    `TurnContextFactory`, `GetChatById`, `RequestSiteAnalysisCapability`, solar,
    `SetSiteBoundaryMembersCapability`. So analyses, the dome and the building-fetch reach use
    the edit (FR-025).
- **First save in a chat**: create the correction, or update the user's existing live correction
  for the same site, and link the chat. The correction stores:
  - a **found snapshot**: the chat's found outline, core polygon and members;
  - the edited rings;
  - the member choice those rings cover.
- **Another chat asks for the same site**: `ResolveSiteBoundaryCapability` calls
  `SiteBoundaryCorrectionMatcher` before any Overpass or vision work. If there is a match, it
  copies the correction's found snapshot into that chat's `ActiveBoundary`, links the correction,
  and returns the effective outline with `userCorrected: true`. No resolution cost, no building
  question and no edit offer are spent (FR-023). The narrator says it is the user's corrected
  outline, and the turn's offer is a reset row plus analysis rows (D6).
- **Reset**: soft-delete the correction and set `CorrectionId = null` on this chat. Every other
  chat linked to it falls back to its own found outline automatically, because a dead link is
  ignored. This satisfies US5 AS2 with no fan-out writes.
- **Edit in chat B after editing in chat A**: both link the same correction, so both see the
  latest rings on their next read. This is "applies in all of the user's chats" with no copying.

**Site identity**: `SiteBoundaryCorrectionMatcher` requires all three:

- the same `UserId`;
- the same normalized site name (lower-case invariant, diacritics folded, whitespace
  collapsed);
- the same place: the requested location point lies inside the correction's found rings grown
  by 100 m, or within 250 m of the found centroid.

Two "City Centre" malls in different cities never match (spec edge case). The lookup is covered
by the filtered index `(UserId, NormalizedSiteName)`. At most one live correction exists per
user, name and place: the save handler updates a match instead of inserting.

**Rationale**: Canonical-plus-found-snapshot keeps reset trivial, gives every chat the latest
edit without writes on read, and keeps the found outline available for a reset in chats that
never resolved it themselves.

**Alternatives considered**:

- *Copy the edited rings into each chat*: a chat reopened after an edit elsewhere would show a
  stale edit, contradicting Q1:B.
- *Overwrite the chat's columns with the edit*: reset would need a separate snapshot column, and
  other chats would still hold copies.
- *Run resolution anyway, then substitute the correction*: wastes Overpass and Gemini calls, and
  would ask a building question the spec says must not repeat.

---

## D6 — Where the edit offer goes, and closing the decline gap

Today, a resolve that finds members ends with the membership offer and then stops (`yield
break`). Its "Keep the outline as it is" row is a client-only `Decline`, which runs no turn. So
nothing can follow it.

**Decision**:

1. **A new deterministic `SiteBoundaryEditOffer`.** It is emitted from `EmitOfferIfDueAsync` when
   the outline became final this turn and is not hand-edited:
   - `resolve_site_boundary` ran with no membership offer (no members, or the setting is off); or
   - `set_site_boundary_members` ran.

   Its rows are:
   - **"Edit the outline"** (`edit_site_boundary`), always first;
   - then the analysis rows the generic `offerGenerator` would have produced this turn, with the
     generator's own question dropped;
   - then **`Decline("It looks right")`**.

   The question states Lucy's certainty in words, keyed on `ConfidenceLevel`:
   - High: "I'm confident about this outline…"
   - Medium: "I'm fairly sure about this outline…"
   - Low: "I'm not sure about this outline…"

   Each ends "…want to adjust its corners?". The generic suppression rules still apply to the
   analysis rows, never to the edit row.
2. **The membership offer's keep row becomes a real row.** It is `set_site_boundary_members` with
   the currently included `memberIds` and `"keep": true`. The capability recognises the no-op,
   returns `kept: true` with the unchanged outline (no redraw event), and the narration shaped
   by that JSON says the outline is kept. Being a real turn, it ends with the edit offer, which
   closes the gap. The ack template becomes "Now updating the site outline.", which reads
   correctly for both keep and change.
3. **A reused correction** (D5) ends with a reset offer instead: "Reset to Lucy's outline"
   (`reset_site_boundary`), then the analysis rows. There is no edit row, since FR-001 forbids
   the edit offer on a hand-edited outline. The map's Edit control still works.

**Rationale**: Showing the edit row first, above the analysis rows, satisfies "before any
analysis suggestions" without dropping them. The alternative, an edit-only offer, would lose
this turn's analysis suggestions entirely, since a decline runs no turn. Turning keep into a real
row reuses an existing capability instead of a new "confirm" capability.

**Alternatives considered**:

- *Put an "Edit by hand" row inside the membership offer*: this mixes two questions, and edits
  made before the building choice would be joined or cut straight away.
- *A client-side follow-up after decline*: offers would stop being server-authored and persisted,
  so they would vanish on reload.
- *A new `confirm_site_boundary` capability*: duplicates `set_site_boundary_members` with the
  same ids.

**Spec amendment B**: US1 AS1 lists the choices as "Edit the outline" and "It looks right". Under
this design they also sit alongside the turn's analysis suggestions, below the edit row. The
membership keep row now runs a short turn, the "Now updating the site outline." acknowledgement
plus a one-line narration, instead of doing nothing.

---

## D7 — Starting edit mode from Lucy: capability → stream command

**Decision**:

- **`edit_site_boundary`**:
  - Duration Brief; Area Location; `IsOfferable` false, since only D6 offers it.
  - `IsAvailable` when `ActiveBoundary` is present. Ownership is enforced in `ExecuteAsync`
    against the chat's `UserId`; a non-owner gets "Only the chat's owner can edit its outline."
    (FR-020, shared-chat edge case).
  - The result JSON is `{siteName, areaSquareMeters, isHandEdited, revision, openEditor: true}`.
  - `StructuredPayloadExtractor` maps it to a new `SiteBoundaryEditCommand` chunk, which reaches
    the client as the SSE event `siteBoundaryEdit`
    ([contract](contracts/site-boundary-edit-sse-event.md)). This is the same route as
    `SolarAnalysisCommand` and `ViewerZoomCommand`.
- **`reset_site_boundary`**:
  - Brief; available when the effective outline is hand-edited.
  - It sends the same `ResetSiteBoundaryCommand` as the REST endpoint, with `expectedRevision`
    set to the current effective revision. A turn never conflicts with itself.
  - It returns the found outline's `SiteBoundaryPayload`. The existing `siteBoundary` event
    redraws it with its animated border.

The map's Edit and Reset buttons don't run a chat turn. Edit opens the session directly from the
store. Reset calls the REST endpoint after an MUI confirm dialog.

**Rationale**: It reuses the capability-to-client command pipeline built for the solar and zoom
commands, and keeps the two surfaces, words and buttons, on one Application command.

**Alternatives considered**: a SignalR push for edit mode. That would bypass the persisted turn,
and the offer row has to work through the normal dispatched-selection path anyway.

---

## D8 — How Lucy knows: the effective outline, a site note, and a persisted line

**Decision**: Three layers, cheapest first.

1. The **effective outline** reaches every capability through `TurnContext.ActiveBoundary` (D5).
   Numbers narrated from capability results are therefore already the edited ones. The narrator
   sees only the result JSON.
2. **`ActiveSiteNote.Describe(TurnContext)`** produces one line added to the fast-path reply and
   decide prompts when an outline is present, for example: *"On the map: Muscat Grand Mall,
   35,210 m², outline hand-edited by the user (this replaces any earlier area)."* This stops the
   model quoting an older found-outline area from history (FR-021, SC-006).
3. **The persisted chat line** (FR-017) is written by the save and reset handlers as an
   `Assistant` `Text` message, from a template, not generated: "You edited the outline of {site}
   — now {area:N0} m²." or "The outline of {site} is back to the one I found — {area:N0} m²."
   - The line is appended in the same unit of work as the correction write (one `SaveChanges`,
     §5).
   - It is returned in the response so the client appends it without refetching.
   - It is also what the spec's "Lucy then states the new area" means. No model call happens on
     save.

**Rationale**: Templated lines are instant, deterministic and free. The site note costs one
prompt line and fixes the history-staleness problem at its source.

**Alternatives considered**:

- *Run a narrated chat turn on Done*: adds latency and cost to a UI action, and the turn could
  fail after the save succeeded.
- *A system-role message*: `MessageRole` has only User and Assistant. Adding a role would touch
  every provider mapping.

---

## D9 — Building choices on a hand-edited outline (Q2:B)

**Decision**: When the effective outline is hand-edited, `SiteBoundaryMembershipService` doesn't
recompose from the core with the raster union. It diffs the member choice against the
correction's `MembersJson`:

- **A separate member added** (farther than `ConnectedGapMeters`, 2 m): its footprint becomes a
  new ring.
- **A connected member added**: `Join`. This computes `edited ∪ footprint ∪ seam`, where
  `seam = buffer(edited, g) ∩ buffer(footprint, g) − (edited ∪ footprint)` with a mitre join.
  Only the gap strip between them is filled, so corners away from the seam are untouched (US4
  AS2).
- **A member removed**: its standalone ring is dropped. If it had been joined, `Cut` computes
  `edited − (footprint ∪ seam)`, then drops slivers thinner than 0.5 m with a
  `buffer(−0.25).buffer(+0.25)` on the removed region only.

The correction's rings, members and revision update. The chat's found outline is also
recomposed from the core as today (raster), so a later reset restores "the found outline with
the building choice in force" (US5).

**Rationale**: A buffer confined to the seam is the standard way to close a gap without
disturbing the rest of either shape.

**Alternatives considered**: *Re-apply the edits as vertex deltas on a freshly composed outline*.
Corner correspondence breaks as soon as the ring topology changes.

---

## D10 — Keyboard and touch (US6, FR-014)

**Decision**: `SiteBoundaryCornerNavigator` is a focusable region next to the toolbar, with
`role="application"` and `aria-roledescription="outline editor"`.

**Moving between corners**:

- Tab and Shift+Tab step through the editable ring's corners with a roving index. Tab past the
  last corner leaves the region, so focus is never trapped.
- `[` and `]` switch rings.
- The selected corner is drawn by a single highlighted `google.maps.Circle`-style marker, not
  one DOM node per corner.
- A polite live region announces "Corner 3 of 12, Muscat Grand Mall – Phase 2".

**Editing keys**:

| Key | Action |
|-----|--------|
| Arrow keys | Move the selected corner 0.5 m north, south, east or west |
| Shift + arrow | Move 5 m |
| Insert or `+` | Add a corner midway to the next corner |
| Delete or Backspace | Delete the selected corner |
| Ctrl/Cmd+Z | Undo |
| Ctrl/Cmd+Shift+Z or Ctrl+Y | Redo |
| Enter on Done | Save |
| Escape | Cancel, with a confirm when there are unsaved changes |

**Touch**: native handles, plus a long-press menu for delete. Map `gestureHandling` stays
`greedy` in edit mode, so one-finger drags on a handle move the corner and elsewhere pan the
map. Toolbar buttons are at least 44×44 px.

Every toolbar control gets jest-axe coverage.

**Rationale**: Native handles aren't focusable. One roving region with a live region is the
standard accessible pattern for canvas-style editors, and it avoids 500 focusable nodes.

---

## D11 — Server validation bounds (FR-020)

**Decision**: `SaveSiteBoundaryEditCommandValidator` (FluentValidation) rejects the save with
400 Problem Details when:

- there are fewer than 1 or more than 20 rings;
- a ring has fewer than 3 or more than 2,000 corners, or the outline has more than 5,000 corners
  in total;
- a latitude is outside [−90, 90], a longitude outside [−180, 180], or a value is not finite.

Rings are closed implicitly. If the client repeats the first corner at the end, the handler drops
the repeat rather than rejecting the save.

The handler then checks geometry with `ISiteRingGeometry`, returning 422 with a
`ringIndex`/`reason` extension when:

- a ring is not simple, has area ≤ 1 m², or has duplicate corners;
- **drift**: a ring doesn't intersect the found outline grown by 25 m;
- **drift**: the union area is more than 3× the found outline's union area.

**Rationale**: The bounds cover the largest traced parks (hundreds of corners) with headroom,
and keep request size and NTS cost bounded. The drift rules implement "a correction must not
turn into a different site".

---

## Spec amendments to apply before `/speckit-tasks`

- **A** (conditional on the D1 spike): US1 AS3's area updates when a corner is dropped, not
  mid-drag.
- **B** (D6): the edit offer shares its row list with the turn's analysis suggestions, below the
  edit row. The building question's "Keep the outline as it is" now runs a short turn.
