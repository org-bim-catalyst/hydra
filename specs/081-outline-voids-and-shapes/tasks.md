# Tasks: Voids and Drawing Shapes in the Outline Editor

**Input**: Design documents in `/specs/081-outline-voids-and-shapes/`: plan.md, spec.md, research.md (D1-D11), data-model.md, contracts/site-boundary-voids-api.md, quickstart.md

**Tests**: Included. Constitution section 10 requires them, and the plan lists them. Write each story's tests first.

**Organization**: by user story.
- US1 (cut makes a void) and US2 (new shapes) are both P1. US1 is the MVP.
- US3 to US6 are P2.

## Format: `[ID] [P?] [Story] Description`

- `C/` means `src/AskLucy.Web/ClientApp/src/`.
- Server paths are relative to the repo root.

---

## Phase 1: Setup

- [X] T001 Re-read the spec 079 code paths this feature extends, so later tasks keep their behaviour: `src/AskLucy.Infrastructure/Boundaries/NtsSiteRingGeometry.cs` (`Combine`, `fromShell`, `LargestShell`, `UnionArea`), `src/AskLucy.Application/SiteBoundaries/HandEditedMembershipComposer.cs`, `C/viewer/siteBoundaryEdit/editablePolygonController.ts` and `C/viewer/siteBoundaryEdit/googleEditablePolygonHost.ts`. Note in `specs/081-outline-voids-and-shapes/research.md` (under D2 and D4) anything that contradicts the plan. If nothing does, record that.

---

## Phase 2: Foundational (blocks every story)

**Purpose**: voids exist end to end in storage, geometry and the wire format, with no UI yet. Outlines without voids must behave exactly as before.

### Domain and persistence

- [X] T002 [P] Add `EditedVoids` (`IReadOnlyList<IReadOnlyList<IReadOnlyList<GeoPoint>>>`, default empty) to `src/AskLucy.Domain/SiteBoundaries/SiteBoundaryCorrection.cs`. Thread a `voids` parameter through `Create`, `ReplaceRings` and `ApplyMembership`; the parameter defaults to empty so existing callers compile. Extend `EnsureShape`: there are no more void lists than rings, and every void has at least 3 corners. Add the reasons `voidOutsidePart`, `voidsTouch`, `nothingChanged` and `tooManyVoids`, and an optional `VoidIndex`, to `src/AskLucy.Domain/SiteBoundaries/SiteBoundaryGeometryRejectedException.cs`.
- [X] T003 [P] Domain tests in `tests/AskLucy.Domain.Tests/SiteBoundaries/SiteBoundaryCorrectionTests.cs`: voids are stored on create and replace; more void lists than rings is rejected; a void with fewer than 3 corners is rejected; a correction without voids behaves as before.
- [X] T004 Add `Voids` (one list per ring index) to `src/AskLucy.Domain/Chats/ActiveSiteBoundary.cs`, filled by `WithCorrection`; a found outline always has empty voids. Do the same in `src/AskLucy.Application/SiteBoundaries/CorrectionOutline.cs` (`ToConfirmed`) and on `ConfirmedSiteBoundaryData` (`src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`). Depends on T002.
- [X] T005 Map `EditedVoids` to a new column, `EditedVoidsJson` (`nvarchar(max)`, not null, default `'[]'`), in `src/AskLucy.Persistence/Configurations/SiteBoundaryCorrectionConfiguration.cs`, with the same JSON converter and comparer as `EditedRingsJson`. Generate the migration `AddSiteBoundaryVoids` with `dotnet tool run dotnet-ef migrations add AddSiteBoundaryVoids --project src/AskLucy.Persistence --startup-project src/AskLucy.Web`. Strip any BOM; `System` usings go first. Depends on T002.
- [X] T006 [P] Persistence test in `tests/AskLucy.Persistence.Tests/SiteBoundaries/SiteBoundaryCorrectionRepositoryTests.cs`: voids survive a save and reload; a row written without the column value reads as having no voids. Depends on T005.
- [X] T007 Apply the migration by hand to the test2 database. Set `ConnectionStrings__DefaultConnection` from `ConnectionStrings:PersistenceTests` in `src/AskLucy.Web/appsettings.Development.json`, then run `dotnet tool run dotnet-ef database update`. Confirm with `migrations list` that it is no longer pending. Do not touch the shared test database. Depends on T005.

### Geometry port

- [X] T008 Extend `src/AskLucy.Application/SiteBoundaries/ISiteRingGeometry.cs`:
  - `UnionArea(rings, voids)`, keeping a no-voids overload;
  - `Combine(rings, voids, shape, op)`, whose `CombineResult` gains `Voids`;
  - `CombineFailure.NothingChanged`;
  - `RingValidationResult.VoidOutsidePart` and `VoidsTouch`;
  - `ValidateVoids(outer, voids)`, returning the first problem and its void index.
- [X] T009 Infrastructure tests first, in `tests/AskLucy.Infrastructure.Tests/Boundaries/NtsSiteRingGeometryCombineTests.cs`:
  - replace `Cutting_ACircleWhollyInside_IsRefused_BecauseItWouldLeaveAHole` with "makes a void";
  - a cut across an existing void opens it into a bite;
  - an Add over a void fills it (wholly and partly);
  - touching voids merge;
  - a split keeps each void with its piece, and a void cut open stops being one;
  - a polygon shape (a rectangle) combines like a circle;
  - a cut inside a void gives `NothingChanged`;
  - results are open, with the site's part first;
  - voids under 1 m² are dropped.

  In `NtsSiteRingGeometryTests.cs`: `UnionArea` subtracts voids, and `ValidateVoids` covers each reason (crossing the edge, touching the edge, two voids touching, a self-crossing void).
- [X] T010 Implement T008 in `src/AskLucy.Infrastructure/Boundaries/NtsSiteRingGeometry.cs`:
  - `ToPolygon(ring, voids, reference)` builds interior rings;
  - `fromShell` becomes `fromPolygon`, returning the outer edge and its holes;
  - remove the `HoleNotSupported` branch from `Combine`;
  - add `NothingChanged`: the result's area and the input's area differ by less than 0.01 m², and they are topologically equal;
  - implement `ValidateVoids` with NTS: `outer.Contains(void)` and `!void.Touches/Intersects(outer boundary)`, and voids pairwise disjoint.

  Make T009 pass. Depends on T008, T009.

### Wire format

- [X] T011 Add `Voids` to `ChatActiveBoundaryDto` (`src/AskLucy.Application/Chats/Queries/GetChatById/ChatDetailDto.cs`, filled from `ActiveSiteBoundary.Voids`). Add `voids` to the `__SITE_BOUNDARY__` event in `src/AskLucy.Web/Controllers/v1/AiController.cs` (`WriteConfirmedBoundaryEventAsync`). Add `voidIndex` to the 422 body in `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`. Depends on T004.
- [X] T012 [P] Client model: add `voids?: GeoPoint[][][]` to `ChatActiveBoundary` in `C/features/chat/api/chatsApi.ts` and to the siteBoundary event in `C/features/chat/api/aiApi.ts`. Add `voids` (default `[]`) and `siteVoidsOf(state)` to `C/store/activeSiteBoundaryStore.ts`. Pass voids through `useChatStream.ts`, `useRestoreChatSite.ts` and `applyBoundaryToViewer` (`C/viewer/siteBoundaryEdit/useSiteBoundaryEditMode.ts`). Test in `C/store/activeSiteBoundaryStore.test.ts`.

**Checkpoint**: everything builds; all existing tests pass; voids round-trip through storage and the API but nothing draws them yet.

---

## Phase 3: User Story 1 - Cut an atrium out of a building (P1, MVP)

**Goal**: a circle cut wholly inside a part becomes a void. It shows as a hole, it is saved, and the area excludes it.

**Independent test**: quickstart scenarios 1, 8 and 11.

### Tests for US1

- [X] T013 [P] [US1] `tests/AskLucy.Application.Tests/Chats/CombineSiteBoundaryShapeCommandTests.cs`: voids pass in and out of the handler; `NothingChanged` maps to 422 `nothingChanged` with "That area is already outside the site."; the `holeNotSupported` mapping test is removed or updated.
- [X] T014 [P] [US1] `tests/AskLucy.Application.Tests/Chats/SaveSiteBoundaryEditCommandTests.cs`:
  - voids are saved;
  - the area equals the union minus the voids;
  - a void crossing its part is refused with 422 `voidOutsidePart`, `ringIndex` and `voidIndex`;
  - touching voids are refused with `voidsTouch`;
  - more than 50 voids in a part is refused with `tooManyVoids`;
  - void corners count toward 5,000;
  - the drift and 3x checks still use outer edges only;
  - a save without voids is unchanged.
- [X] T015 [P] [US1] Client `C/viewer/siteBoundaryEdit/siteBoundaryEditStore.test.ts`: the session holds voids; `replaceAll` with voids is one undo step and undo restores them; `totalArea` subtracts voids.

### Implementation for US1

- [X] T016 [US1] Combine command: add `Voids` (optional) to `CombineSiteBoundaryShapeRequest` (`src/AskLucy.Web/Contracts/ChatContracts.cs`), to `CombineSiteBoundaryShapeCommand`, to its validator (each void has 3 to 2,000 corners; at most 50 per part; void corners count toward the total) and to the handler (validate each void with `Validate` and `ValidateVoids`; map `NothingChanged`; return voids). `ChatsController` passes them through. Depends on T010.
- [X] T017 [US1] Save command: add `Voids` to `SaveSiteBoundaryEditRequest`, `SaveSiteBoundaryEditCommand` and `SaveSiteBoundaryEditCommandValidator` (add `MaxVoidsPerPart = 50`). In the handler (`EnsureAcceptable`), run `ValidateVoids` per part, throwing `SiteBoundaryGeometryRejectedException` with `RingIndex` and `VoidIndex`. Compute the area with `UnionArea(rings, voids)`. Pass the voids to `Create` and `ReplaceRings`. Depends on T010, T011.
- [X] T018 [US1] Client geometry in `C/viewer/siteBoundaryEdit/ringGeometry.ts`: add `pointInRing`, `ringsTouch` (reusing the existing segment-crossing helpers) and `validateVoid(outer, voids, k)`, with reasons `voidOutsidePart` and `voidsTouch` and their messages. Tests go in `ringGeometry.test.ts`.
- [X] T019 [US1] Edit session in `C/viewer/siteBoundaryEdit/siteBoundaryEditStore.ts`:
  - add `voids` and `startVoids` to the session (`enter` takes voids);
  - `replaceAll` carries `{ rings, voids }`;
  - `isDirty` and `rebase` include voids;
  - `totalArea` subtracts voids;
  - add `activePath` (0 is the outer edge), which `setActiveRing` resets.
- [X] T020 [US1] Editable host and controller, voids shown but not yet edited:
  - `C/viewer/siteBoundaryEdit/googleEditablePolygonHost.ts`: `createRing(corners, voids, { editable })` builds `paths: [outer, ...voids]`, each void reversed to the opposite winding with `isCounterClockwise`.
  - `C/viewer/siteBoundaryEdit/editablePolygonController.ts`: `mount`, `setRings` and `replaceAllRings` take and redraw voids.

  Update the fake host in `editablePolygonController.test.ts`. Depends on T019.
- [X] T021 [US1] Circle cut sends and receives voids. In `C/viewer/siteBoundaryEdit/useSiteBoundaryEditMode.ts` (`applyCircle`), send `voids` and apply `result.voids` through `replaceAllRings`. Save sends `voids`; a 422 with `voidIndex` makes that part active and shows the message. In `C/features/chat/api/chatsApi.ts`, add `voids` to the combine and save requests and responses. Depends on T016, T017, T020.
- [X] T022 [US1] Normal display:
  - `C/viewer/layers/gis/GoogleMapsGisLayer.ts` `setSiteBoundary`: accept `voids`, so each part's native polygon has `paths: [outer, ...reversed voids]`.
  - `C/viewer/layers/gis/SiteBoundaryRenderer.ts`: also draw void borders.
  - `C/features/viewer/components/SiteBoundaryOverlay.tsx`: pass `siteVoidsOf(state)`.

  Tests go in `GoogleMapsGisLayer.test.ts` and `SiteBoundaryRenderer.test.ts`.

**Checkpoint**: on a real site, a circle cut inside the outline shows a hole, the area drops, and the void survives Done and a reload. Saved outlines without voids are unchanged.

---

## Phase 4: User Story 2 - Rectangle, square and free polygon (P1)

**Goal**: every new shape adds or cuts like the circle.

**Independent test**: quickstart scenarios 2, 3, 4 and 12.

### Tests for US2

- [X] T023 [P] [US2] `CombineSiteBoundaryShapeCommandTests.cs`: a `shape` polygon combines; exactly one of circle or shape is required (400 for neither and for both); a shape with fewer than 3 or more than 2,000 corners, or one that crosses itself, is refused.
- [ ] T024 [P] [US2] Client `C/features/viewer/components/SiteBoundaryShapeDraw.test.tsx` (moved from `SiteBoundaryCircleDraw.test.tsx` and extended):
  - circle behaviour unchanged;
  - rectangle preview corners follow the drag;
  - the square keeps equal sides;
  - previews use px or SVG coordinates, never bare fractions;
  - a drag that is too small is treated as a click;
  - apply sends the polygon.
- [ ] T025 [P] [US2] Client `C/features/viewer/components/SiteBoundaryPolygonDraw.test.tsx`:
  - clicks add corners;
  - a double-click, a click on the first corner within 10 px, and Enter each finish;
  - Backspace removes the last corner;
  - Escape cancels;
  - a crossing polygon, or one with fewer than 3 corners, is refused with a message and nothing is sent;
  - Space adds a corner at the map centre.

### Implementation for US2

- [X] T026 [US2] Server: `CombineSiteBoundaryShapeRequest` and the command gain an optional `Shape` (`IReadOnlyList<GeoPoint>`). The validator requires exactly one of (`Centre` and `RadiusMeters`) or `Shape`. The handler uses `Shape` as the combine shape after `Validate`. Update the OpenAPI expectations in `tests/AskLucy.Web.Tests/Chats/SiteBoundaryEditEndpointTests.cs` if needed. Depends on T016.
- [ ] T027 [US2] Store and actions:
  - In `C/viewer/siteBoundaryEdit/siteBoundaryEditStore.ts`, replace the `circle` tool and `circleOperation` with the `shape` tool plus `shapeKind: 'circle' | 'rectangle' | 'square'` and `shapeOperation: 'add' | 'cut'`.
  - Add a `polygon` tool with `polygonOperation` and `polygonCorners`.
  - `beginCircle` becomes `beginShape(kind, op)`; add `beginPolygon(op)`.
  - In `C/viewer/siteBoundaryEdit/siteBoundaryEditActions.ts`, replace `startCircle`/`applyCircle` with `startShape(kind, op)` and `applyShapePolygon(points)` (the circle still sends centre and radius).
  - Update every caller and its tests.
- [ ] T028 [US2] `C/viewer/siteBoundaryEdit/ringShapes.ts`: add `rectangleRing(a, b)` (north-south and east-west sides) and `squareRing(a, b)` (the larger side, growing towards the drag), with tests in `ringShapes.test.ts`.
- [ ] T029 [US2] Rename `C/features/viewer/components/SiteBoundaryCircleDraw.tsx` to `SiteBoundaryShapeDraw.tsx` and generalise it by `shapeKind`. The preview is an SVG polygon of `circleRing`, `rectangleRing` or `squareRing` projected through `toPixel`. The hint line reads "W × H m - release to add it / cut it out", or "Radius N m" for the circle. Applying runs `applyShapePolygon` (rectangle and square) or `applyCircle`. Mount it in `SiteBoundaryEditHost.tsx`. Depends on T027, T028.
- [ ] T030 [US2] New `C/features/viewer/components/SiteBoundaryPolygonDraw.tsx`:
  - follow the layer pattern of `SiteBoundaryBoxSelect.tsx` (zIndex 4, `userSelect: 'none'`, `onMouseDown` preventDefault, wheel zoom forwarded);
  - placed corners plus a rubber-band edge to the pointer, as an SVG polyline;
  - finishes as described in T025; `validateRing` runs before sending, and a refusal goes through `store.refuse`;
  - keyboard: a crosshair at the map centre, Space adds a corner there, and the arrow keys pan the map;
  - touch: a tap adds a corner;
  - add a hint line, and mount it in `SiteBoundaryEditHost.tsx`.

  Depends on T027.
- [ ] T031 [US2] `C/features/viewer/components/SiteBoundaryShapeDialog.tsx`: add Rectangle (width and height in metres) and Square (side), placed at `ringCentre` of the active part, with an Add or Cut choice. Make part a circle stays. Tests go in `SiteBoundaryShapeDialog.test.tsx`. Depends on T026, T027.
- [ ] T032 [US2] Icons in `C/features/chat/outlineToolIcons.tsx`, in the existing 24×24, stroke-2, round-cap style: Add rectangle, Cut rectangle, Add square, Cut square, Add polygon, Cut polygon, Remove void, and an Edit-group icon if needed. Add temporary flat entries in `C/features/chat/OutlineActionGroup.tsx` so the tools are reachable before US6 groups them.

**Checkpoint**: all four shapes add and cut with the mouse and the keyboard; each is one undo step.

---

## Phase 5: User Story 3 - Adjust or remove a void (P2)

**Goal**: void corners are edited like outer corners; a void can be removed.

**Independent test**: quickstart scenarios 5 and 6.

- [ ] T033 [P] [US3] Tests:
  - `editablePolygonController.test.ts`: void corner move, insert and delete; refusals for crossing the edge, touching another void, self-crossing, and fewer than 3 corners; an outer-edge move that would cut through a void is refused; undo and redo of void edits.
  - `siteBoundaryEditStore.test.ts`: path-addressed changes; `removeVoid` and its undo.
  - `SiteBoundaryCornerNavigator.test.tsx`: Shift+[ and Shift+] cycle the outer edge and the voids, with the "void k of n" announcement.
  - `SiteBoundaryCornerMenu.test.tsx`: Remove void appears only for void corners.
- [X] T034 [US3] Store: `RingChange` move, moveMany, insert, delete and replace gain `path` (default 0); `applyForward` and `applyBackward` address `voids[ring][path-1]` when `path > 0`; add `setActivePath`, `removeVoid(ring, k)` and its `addVoid` undo. File: `C/viewer/siteBoundaryEdit/siteBoundaryEditStore.ts`. Depends on T019.
- [X] T035 [US3] Host and controller:
  - The host adapts every path with `polygon.getPaths().getAt(k)`. Vertex events (click, press, drag-move, menu) report `PolyMouseEvent.path` and `vertex`, and the corner rings overlay is per path.
  - The controller's `Mounted.known` becomes per path. `set_at`, `insert_at` and `remove_at` on a void run `validateChange` plus `validateVoid`; on the outer edge they also check that every void is still inside.
  - `insertCornerAfter`, `moveCorner(s)` and `deleteCorner(s)` take a path. A click on a void corner sets `activePath`.

  Files: `googleEditablePolygonHost.ts`, `editablePolygonController.ts`. Depends on T020, T034.
- [ ] T036 [US3] Paths everywhere a corner is addressed:
  - `C/features/viewer/components/SiteBoundaryCornerNavigator.tsx`: Shift+[ and Shift+] switch paths; Tab walks the active path; the arrow keys nudge.
  - `SiteBoundaryCornerMenu.tsx`: Remove void.
  - `SiteBoundaryBoxSelect.tsx`: corners of the active path only.
  - `followCornerCursor` in `useSiteBoundaryEditMode.ts`: the active part's every path.
  - `siteBoundaryEditActions.nudgeCorner`.

  Depends on T035.

**Checkpoint**: quickstart scenarios 5 and 6 pass.

---

## Phase 6: User Story 4 - Splits keep voids with their piece (P2)

**Goal**: a cut that splits a part leaves separate parts, and each void stays with its piece.

**Independent test**: quickstart scenario 7.

- [ ] T037 [US4] Confirm, with an Infrastructure test (from T009) and a client test in `useSiteBoundaryEditMode.test.tsx`, that a split returns parts in order with their voids; `[` and `]` reach every new part, and Undo restores the original single part with its voids. Fix any gap in `fromPolygon` ordering or in the client's `replaceAllRings`. Depends on T021.

---

## Phase 7: User Story 5 - Lucy knows about voids (P2)

**Goal**: Lucy reports the area excluding voids, and can say how many voids there are.

**Independent test**: quickstart scenario 9.

- [X] T038 [P] [US5] Tests: `tests/AskLucy.Application.Tests/SiteBoundaries/SiteBoundaryPayloadTests.cs` (writes and reads `voids`, `voidCount` and `voidAreaSquareMeters`); `tests/AskLucy.Application.Tests/Conversations/Runtime/ActiveSiteNoteTests.cs` (the note mentions voids only when there are some); `EditSiteBoundaryCapabilityTests.cs` (`voidCount`).
- [X] T039 [US5] Implement in `src/AskLucy.Application/SiteBoundaries/SiteBoundaryPayload.cs`, `src/AskLucy.Application/Conversations/Runtime/ActiveSiteNote.cs` ("with N void(s), X m² excluded") and `src/AskLucy.Application/Conversations/Capabilities/EditSiteBoundaryCapability.cs`. The void area is computed with `UnionArea` of the voids alone (a small helper on the geometry port, or `GeometryMath.AreaSquareMeters` summed). Depends on T004.
- [X] T040 [US5] Membership keeps voids (research D7). Tests first in `tests/AskLucy.Application.Tests/SiteBoundaries/HandEditedMembershipComposerTests.cs`:
  - adding or removing a building keeps the part's voids;
  - a building added over a void fills that part of it;
  - adding a U-shaped building makes a courtyard void instead of failing;
  - the member list is identical with and without voids.

  Then implement in `HandEditedMembershipComposer.cs` and in `SetSiteBoundaryMembersCapability.cs` (pass voids to `ApplyMembership`). Depends on T010.

---

## Phase 8: User Story 6 - Grouped tools in the edit ribbon (P2)

**Goal**: Photoshop-style tool groups with vertical sub-menus.

**Independent test**: quickstart scenarios 13, 14 and 15.

- [ ] T041 [P] [US6] Tests in `C/components/workspace-shell/ExpandableActionGroup.test.tsx`:
  - a group renders one button with the active tool's icon and the corner mark;
  - a click activates the shown tool;
  - a click while active, a right-click, press-and-hold (400 ms, fake timers) and the corner mark each open the menu;
  - choosing calls `onChooseTool` and closes the menu;
  - disabled tools show their reason;
  - keyboard: Down arrow opens, arrows move, Enter chooses, Escape returns focus;
  - `aria-haspopup` and `aria-expanded` are set.

  Add an a11y test (`ExpandableActionGroup.a11y.test.tsx`) with the menu open, using `getByText` inside the popper.
- [ ] T042 [US6] Implement group actions in `C/components/workspace-shell/ExpandableActionGroup.tsx` (research D11): an optional `tools` array, `activeToolId` and `onChooseTool` on an action, rendered as the 40 px round button with a corner triangle and an MUI `Popper` plus a vertical `MenuList` that flips upwards when there's no room. Plain actions render exactly as today, so the other ribbons are unchanged.
- [ ] T043 [US6] New `C/features/chat/outlineToolGroupStore.ts`: a Zustand store of the last-chosen tool per group, persisted to `localStorage` (key `outline-tool-groups`), with defaults (Edit corners, Add corner, Curve edge, Add circle, Add rectangle, Add polygon). Test it in `outlineToolGroupStore.test.ts`.
- [ ] T044 [US6] Rebuild `C/features/chat/OutlineActionGroup.tsx` as the six groups plus Undo, Redo, Cancel, Reset and Done (spec US6 AS1), replacing the temporary flat entries from T032.
  - Each group is highlighted when the session's active tool belongs to it.
  - One-off actions (Make part a circle, Round corner, Remove void, Delete corner) run and become the group's shown tool.
  - Existing disabled reasons are kept.

  Add `OutlineActionGroup.test.tsx`: 11 buttons; choosing Cut circle highlights Circle with the Cut circle icon; choosing Delete corner moves the highlight; the remembered tool is restored. Depends on T042, T043, T032.

**Checkpoint**: the ribbon shows 11 buttons, and every tool is reachable in at most two clicks.

---

## Phase 9: Polish and cross-cutting

- [ ] T045 [P] Docs:
  - `docs/DATABASE.md`: `EditedVoidsJson`.
  - `docs/API_GUIDELINES.md` or the 079 contract `specs/079-site-boundary-manual-editing/contracts/site-boundary-edit-api.md`: superseded notes pointing to `specs/081-outline-voids-and-shapes/contracts/site-boundary-voids-api.md` (holeNotSupported is no longer produced by the editor; `shape` and `voids` were added).
  - `specs/079-site-boundary-manual-editing/spec.md` Decisions: "voids were added by 081".
- [ ] T046 Full verification:
  - `dotnet build`, and `dotnet format --verify-no-changes` (ENDOFLINE noise aside);
  - Domain, Application, Infrastructure and Web tests (`PERSISTENCE_TESTS_CONNECTION_STRING` set), and Persistence tests against test2;
  - in `ClientApp`: `npx tsc -b --noEmit`, `npx eslint .`, and the full `npx vitest run` (also with `MSYS_NO_PATHCONV=1 VITE_API_BASE_URL=/api/v1`).
- [ ] T047 Push to main, watch CI, and confirm the deploy by fetching the live bundle and searching for new strings (for example "Cut rectangle" and "already outside the site").
- [ ] T048 Walk quickstart scenarios 1 to 15 on production (hydra, Al Safa Park 2) in a tab in the Claude in Chrome group. Record pointer and box events live if anything misbehaves. Then mark the spec Implemented and record the walkthrough in `specs/081-outline-voids-and-shapes/spec.md`.

---

## Dependencies and execution order

- Phase 2 blocks everything. Inside it:
  - T002 → T004 → T011;
  - T002 → T005 → T006 and T007;
  - T008 → T009 → T010.
- **US1** (T013 to T022) needs Phase 2. It is the MVP.
- **US2** needs T016 (combine with voids) and T019 (session). It can run alongside US3 to US5 once US1's T021 is done.
- **US3** needs T019 and T020.
- **US4** needs T021.
- **US5**: T039 needs T004; T040 needs T010.
- **US6** needs T032 (the icons) and touches only the ribbon, so it can run in parallel with US3 to US5.
- Polish comes last.

```text
T002→T004→T011→T017→T021→T037
T002→T005→T007
T008→T009→T010→T016→T026→T029/T030/T031
T019→T020→T021 ; T019→T034→T035→T036
T032→T044 ; T042,T043→T044
```

## Parallel examples

- **Phase 2:** T002, T008 and T012 start together; then T003 and T009.
- **US1:** tests T013, T014 and T015 together; T018 alongside T016 and T017.
- **US2:** tests T023, T024 and T025 together; T028 alongside T026.
- **After US1:** US3 (T033 to T036), US5 (T038 to T040) and US6 (T041 to T044) can proceed in parallel. The shared files (`siteBoundaryEditStore.ts`, `OutlineActionGroup.tsx`) are merged one after another.

## Implementation strategy

1. **MVP:** Phase 1, Phase 2 and US1. Push: voids work with the circle, and nothing else changes.
2. **US2 (shapes):** push. **US6 (grouped ribbon):** push, because the ribbon becomes crowded once US2 lands.
3. **US3, US4 and US5:** push.
4. **Polish:** walk through on production.

At each push: apply the migration to test2 first (only once, at Phase 2), push to main, watch CI, and confirm the live bundle.
