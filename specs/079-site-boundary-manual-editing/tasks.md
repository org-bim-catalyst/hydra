# Tasks: Hand-Edit the Site Outline

**Input**: Design documents from `/specs/079-site-boundary-manual-editing/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included and **not optional**. Constitution §10 requires tests for new behaviour in the
same change that introduces it. SC-003 and SC-007 can only be claimed from tests.

**Organization**: Grouped by user story. Three stories are P1:

- US1 (offer, edit mode, move, Done, view restore) is the MVP.
- US2 (add and delete corners) builds on US1's editor.
- US3 (sticks in every chat, Lucy knows) builds on US1's save.

US4 and US5 (P2) depend on US3's correction reuse. US6 (P3) depends on US1 and US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story the task belongs to (US1–US6)
- Exact file paths are given in every task

## Path Conventions

- Domain, Application, Infrastructure, Persistence, Web: `src/AskLucy.<Layer>/`
- Backend tests: `tests/AskLucy.<Layer>.Tests/`, mirroring the source folders
- Frontend: `src/AskLucy.Web/ClientApp/src/`; the new edit-mode code is in `viewer/siteBoundaryEdit/`
- Frontend tests live beside their subject (`*.test.ts` / `*.test.tsx`, `*.a11y.test.tsx`)

**Verification commands** (run continuously, not only at the end):

```bash
dotnet build "Ask Lucy.sln"                                          # note the space in the name
cd "src/AskLucy.Web/ClientApp" && npx tsc -b --noEmit && npm test    # NOT bare `tsc --noEmit`
```

---

## Phase 1: Setup

**Purpose**: The one new dependency, and the spike that decides spec amendment A.

- [x] T001 Add the `NetTopologySuite` package reference (latest stable 2.x) to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj` only. Confirm that `dotnet build "Ask Lucy.sln"` passes and that no other project references it (research D3).
- [ ] T002 [P] Spike (research D1): in a scratch page under the Vite dev harness, make a `google.maps.Polygon` with `editable: true` on the vector map. Record three things: whether the path's `set_at` fires continuously during a vertex drag or only on drop; whether `contextmenu` and long-press give `PolyMouseEvent.vertex`; and whether handles work at tilt 0 / heading 0. Write the findings in `specs/079-site-boundary-manual-editing/research.md` under D1 "Spike result", and apply or strike spec amendment A in `spec.md` US1 AS3. Delete the scratch page afterwards.

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The data model, the outline in force, the geometry port and client state. Every
story depends on these.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

### Domain

- [x] T003 [P] Add `UserCorrected` to `src/AskLucy.Domain/SiteBoundaries/SiteBoundarySource.cs`, with an XML doc saying it is only ever the source of an effective (outline-in-force) boundary.
- [x] T004 [P] Create the `FoundSiteBoundarySnapshot` value object in `src/AskLucy.Domain/SiteBoundaries/FoundSiteBoundarySnapshot.cs`: polygon, additional polygons, core polygon, area, confidence, confidence level, source, source detail and members (data-model.md).
- [x] T005 Create the `SiteBoundaryCorrection` aggregate in `src/AskLucy.Domain/SiteBoundaries/SiteBoundaryCorrection.cs`. It has `Create`, `ReplaceRings`, `ApplyMembership` and `Delete`, the invariants (at least one ring, at least 3 corners per ring, area > 0, non-empty `UserId`), a new `Revision` on every mutation, and a `NormalizeSiteName` static (lower-case invariant, diacritics folded, whitespace collapsed).
- [x] T006 Extend `src/AskLucy.Domain/Chats/ActiveSiteBoundary.cs` with `Revision` (Guid), `CorrectionId` (Guid?), `IsHandEdited`, and a pure `WithCorrection(SiteBoundaryCorrection)`. `WithCorrection` replaces polygon, additional polygons, area, members and revision, sets source to `UserCorrected` and confidence level to High, and keeps the site name, core polygon and centroid.
- [x] T007 Extend `src/AskLucy.Domain/Chats/UserChat.cs` with `LinkSiteBoundaryCorrection(Guid)` and `UnlinkSiteBoundaryCorrection()`. Make every existing outline-recording path set a new `Revision`, and make recording a **different** site clear `CorrectionId` (FR-024).
- [x] T008 [P] Domain tests in `tests/AskLucy.Domain.Tests/SiteBoundaries/SiteBoundaryCorrectionTests.cs`: invariants, revision changes on each mutation, and name normalisation ("Muscat  Grand Mall" = "muscat grand mall", "Café" = "cafe").
- [x] T009 [P] Domain tests in `tests/AskLucy.Domain.Tests/Chats/ActiveSiteBoundaryCorrectionTests.cs`: which fields `WithCorrection` replaces and keeps; link and unlink regenerate the revision; a different site clears `CorrectionId`; the same site keeps it.

### Persistence

- [x] T010 Create `src/AskLucy.Persistence/Configurations/SiteBoundaryCorrectionConfiguration.cs`. It maps the JSON columns (as the `ActiveBoundary*Json` columns in `UserChatConfiguration` do), the max lengths from data-model.md, `RowVersion`, the soft-delete query filter, and the filtered index `IX_SiteBoundaryCorrections_UserId_NormalizedSiteName` (`DeletedAtUtc IS NULL`). Add the `DbSet` to the context.
- [x] T011 Extend `src/AskLucy.Persistence/Configurations/UserChatConfiguration.cs` with the owned `ActiveBoundaryRevision` and `ActiveBoundaryCorrectionId` columns, plus the filtered index `IX_UserChats_ActiveBoundaryCorrectionId` (`IS NOT NULL`). Add no DB foreign key (data-model.md).
- [x] T012 Generate the migration `AddSiteBoundaryCorrections` (it sorts after spec 067\'s `20260928041922_AddNotificationHub`, so build on that snapshot) in `src/AskLucy.Persistence/Migrations/`. Hand-add the backfill `UPDATE UserChats SET ActiveBoundaryRevision = NEWID() WHERE ActiveBoundarySiteName IS NOT NULL` to `Up`, and check that `Down` drops everything. Check the file's BOM and CRLF, and put `System` usings first (memory: CI gotchas).
- [x] T013 Create the port `ISiteBoundaryCorrectionRepository` in `src/AskLucy.Application/SiteBoundaries/ISiteBoundaryCorrectionRepository.cs`, with `GetByIdAsync(id, userId)`, `FindCandidatesAsync(userId, normalizedName)` and `Add`. Implement it in `src/AskLucy.Persistence/Repositories/SiteBoundaryCorrectionRepository.cs`: every query is filtered by `UserId`, and a save race surfaces as `DbUpdateConcurrencyException`, which the middleware already maps to 409 (no translation here). Register it in `src/AskLucy.Persistence/DependencyInjection.cs`.
- [x] T014 [P] Persistence tests in `tests/AskLucy.Persistence.Tests/SiteBoundaries/SiteBoundaryCorrectionRepositoryTests.cs`: JSON round-trip within 1e-9°, the soft-delete filter, the user scoping (another user's row is never returned), and the new `UserChats` columns round-tripping.
- [x] T015 Create `ChatOwnershipAuditor` in `src/AskLucy.Application/Chats/Authorization/ChatOwnershipAuditor.cs` (scoped). `EnsureOwnedByAsync(chat, userId, operation, ct)`: when the chat exists but belongs to someone else, write `RoleAuditLog.Record(RoleAuditAction.AuthorizationDenied, userId, detailsJson: {chatId, operation})` through `IRoleAuditLogRepository` and save, then throw `KeyNotFoundException("Chat not found.")`; a missing chat throws without a row. Constitution §8 requires this; do NOT use the notification audit trail from spec 067. Register it. Test in `tests/AskLucy.Application.Tests/Chats/ChatOwnershipAuditorTests.cs`: a non-owner writes one row; a missing chat and the owner write none.

### Geometry port

- [x] T016 Create the port `ISiteRingGeometry` in `src/AskLucy.Application/SiteBoundaries/ISiteRingGeometry.cs`. It has `Validate(ring)` returning a `RingValidationResult` (`Ok` | `SelfCrossing` | `Degenerate` | `DuplicateCorner`), `UnionArea(rings)`, `Intersects(rings, foundRings, growMeters)`, and `Join` / `Cut` signatures (implemented in US4) (research D3).
- [x] T017 Create `SiteBoundaryGeometryRejectedException(int RingIndex, string Reason)` in `src/AskLucy.Domain/SiteBoundaries/SiteBoundaryGeometryRejectedException.cs` (`Reason`: selfCrossing | degenerate | duplicateCorner | driftedAway | tooLarge). Give `ConcurrencyConflictException` in `src/AskLucy.Domain/Common/ConcurrencyConflictException.cs` an optional `CurrentRevision`. In `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs` map the new exception to 422 (type `.../problems/site-boundary-rejected`) with `ringIndex` and `reason` extensions, and add a `currentRevision` extension to the existing 409 arm when set. Extend the middleware tests.
- [x] T018 Implement `Validate`, `UnionArea` and `Intersects` in `src/AskLucy.Infrastructure/Boundaries/NtsSiteRingGeometry.cs`, in a local metric frame around the rings' centroid (the same projection as `GeometryMath`). Leave `Join` and `Cut` throwing `NotImplementedException` until T073. Register it as a singleton in `src/AskLucy.Infrastructure/DependencyInjection.cs`.
- [x] T019 [P] Infrastructure tests in `tests/AskLucy.Infrastructure.Tests/Boundaries/NtsSiteRingGeometryTests.cs`:
  - a bow-tie ring is `SelfCrossing`;
  - a collinear ring and a < 1 m² ring are `Degenerate`;
  - corners 0.02 m apart are `DuplicateCorner`;
  - two 100 × 100 m squares overlapping by half have a union area of about 15,000 m² (±0.5%);
  - `Intersects` is true 20 m away with 25 m of growth, and false at 40 m.

### Outline in force

- [x] T020 Create `EffectiveSiteBoundary` in `src/AskLucy.Application/SiteBoundaries/EffectiveSiteBoundary.cs`. `ResolveAsync(UserChat)` returns `ActiveBoundary.WithCorrection(c)` when `CorrectionId` points at a live correction owned by the chat's `UserId`; otherwise the found `ActiveBoundary`, treating a dead link as none. Register it scoped.
- [x] T021 Route every outline reader through `EffectiveSiteBoundary`: The other readers: `TurnDecider` (via `TurnContext`); `LocateAPlaceFlow` and `RecordActiveLocationCommandHandler` only compare site names and need no change; `SetSiteBoundaryMembersCapability` waits for US4. The solar dome and surrounding-building fetch read `activeSiteBoundaryStore`, which gets the edited rings via T025 and T049; assert that in the T025 test.
  - `TurnContextFactory` (callers in `src/AskLucy.Application/Conversations/Runtime/ConversationTurnOrchestrator.cs`, `RetryTargetResolver.cs` and `SelectedActionResolver.cs`);
  - `src/AskLucy.Application/Conversations/Capabilities/RequestSiteAnalysisCapability.cs`;
  - `OpenSolarAnalysisCapability.cs`;
  - any other `chat.ActiveBoundary` reader found by grep.

  This covers FR-025. Leave `SetSiteBoundaryMembersCapability` to US4.
- [x] T022 Extend `src/AskLucy.Application/Chats/Queries/GetChatById/ChatDetailDto.cs` with `ChatActiveBoundaryDto.Revision` and `IsHandEdited`, built from the effective outline. The handler resolves it through `EffectiveSiteBoundary` (contracts/site-boundary-edit-api.md).
- [x] T023 [P] Application tests in `tests/AskLucy.Application.Tests/SiteBoundaries/EffectiveSiteBoundaryTests.cs`: no link gives the found outline; a live link gives the corrected one; a deleted correction gives the found outline; another user's correction id gives the found outline (FR-026).
- [x] T024 [P] Extend `tests/AskLucy.Application.Tests/Chats/GetChatByIdQueryHandlerTests.cs`: `revision` and `isHandEdited` are present, and the effective outline is returned when linked.

### Client state and local geometry

- [x] T025 [P] Add `revision: string` and `isHandEdited: boolean` to `src/AskLucy.Web/ClientApp/src/store/activeSiteBoundaryStore.ts`. Populate them wherever the store is filled: the SSE `siteBoundary` event in `features/chat/hooks/useChatStream.ts` and chat detail hydration, with the zod schemas updated in `features/chat/api/chatsApi.ts`. Extend `store/activeSiteBoundaryStore.test.ts`.
- [x] T026 [P] Create `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/ringGeometry.ts`: ENU projection, `ringAreaSquareMeters`, `validateChange(ring, index)` (O(n): only the edges touching `index`, against the rest), `validateRing`, the 3-corner minimum, the 1 m² floor and the 0.05 m duplicate check. It returns the refusal messages from contracts/edit-mode-viewer.md.
- [x] T027 [P] Tests in `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/ringGeometry.test.ts`: the same fixtures as T019 (bow-tie, degenerate, duplicate, areas within 0.5% of the backend); a 500-corner ring validates one change in under 2 ms.
- [x] T028 Create the `siteBoundaryEditStore` (module-level Zustand) in `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/siteBoundaryEditStore.ts`, following the `SiteBoundaryEditSession` shape and state transitions in data-model.md. It provides `enter`, `applyChange`, `refuse`, `undo`, `redo`, `cancel`, `beginSave`, `saveSucceeded`, `saveFailed`, `conflict`, `forceExit` and `setActiveRing`, with no map dependency.
- [x] T029 [P] Tests in `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/siteBoundaryEditStore.test.ts`: every transition in data-model.md; undo and redo order; cancel restores `startRings`; a new change clears redo; state survives a simulated unmount (it is module-level).

**Checkpoint**: The outline in force exists end to end. Existing behaviour is unchanged: every
test passes, and chat detail now carries `revision` and `isHandEdited: false`.

---

## Phase 3: User Story 1 - Lucy offers an edit, and the view comes back afterwards (Priority: P1) 🎯 MVP

**Goal**: After the outline is final, Lucy offers "Edit the outline". Accepting enters a
north-up plan editor. The user moves corners, and Done saves them, redraws the outline with
effects and restores the exact prior view.

**Independent Test**: quickstart steps 1, 2, 5 and 10. Resolve Muscat Grand Mall in 3D while
rotating, pick B, accept the edit offer, move one corner and press Done. The outline keeps the
corner, the area changes, and the view is 3D, rotating, at the same zoom and heading within 1°.

### Tests for User Story 1

- [x] T030 [P] [US1] Create `tests/AskLucy.Application.Tests/Conversations/Runtime/SiteBoundaryEditOfferTests.cs`: the question wording per confidence level (High, Medium, Low); the edit row first, then the analysis rows, then the `Decline("It looks right")` row; no offer when the outline is hand-edited.
- [x] T031 [P] [US1] Extend `tests/AskLucy.Application.Tests/Conversations/Runtime/SiteBoundaryMembershipOfferTests.cs`: the last row is `set_site_boundary_members` with the currently included `memberIds` and `keep: true`, labelled "Keep the outline as it is", no longer a `Decline`.
- [x] T032 [P] [US1] Create `tests/AskLucy.Application.Tests/Conversations/Runtime/ConversationTurnOrchestratorSiteBoundaryOfferTests.cs`, covering precedence rows 2–4 from contracts/edit-and-reset-capabilities.md:
  - a resolve with members gives the membership offer only;
  - a resolve without members gives the edit offer;
  - `set_site_boundary_members` (a change or a keep) gives the edit offer;
  - any other turn gives the generic offer;
  - the generator's analysis rows are appended after the edit row.
- [x] T033 [P] [US1] Create `tests/AskLucy.Application.Tests/Conversations/Capabilities/EditSiteBoundaryCapabilityTests.cs`: the owner gets `openEditor: true` with the revision; a non-owner fails with "Only the chat's owner…"; no outline fails.
- [x] T034 [P] [US1] Extend `tests/AskLucy.Application.Tests/Conversations/Capabilities/SetSiteBoundaryMembersCapabilityTests.cs`: `keep: true` with the unchanged ids returns `kept: true` and no geometry change. Assert that the first field of the result data is that plain sentence.
- [x] T035 [P] [US1] Create `tests/AskLucy.Application.Tests/Conversations/Runtime/StructuredPayloadExtractorSiteBoundaryEditTests.cs`: an `edit_site_boundary` result gives `ChatStreamChunk.SiteBoundaryEdit` with the chat id and revision.
- [x] T036 [P] [US1] Create `tests/AskLucy.Application.Tests/Chats/SaveSiteBoundaryEditCommandTests.cs`, covering the validator and handler: A non-owner attempt writes one `AuthorizationDenied` audit row.
  - validation: ring and corner bounds, coordinate ranges, a repeated closing corner being dropped, (amended: the ring count may differ from the outline in force - circle Add/Cut, see contracts);
  - ownership: a non-owner gets `KeyNotFoundException`;
  - a revision mismatch throws `ConcurrencyConflictException` with the current revision;
  - `ISiteRingGeometry` refusals map to a 422-type result with `ringIndex` and `reason`;
  - drift: no intersection within 25 m, and a union area over 3× the found area;
  - the first save creates a correction, links the chat and appends one Assistant message "You edited the outline of {site} — now {area:N0} m²";
  - all of it in one `SaveChanges`.
- [x] T037 [P] [US1] Create `tests/AskLucy.Web.Tests/Chats/SiteBoundaryEditEndpointTests.cs` for `PUT /api/v1/chats/{id}/site-boundary`: 200 with the contract shape, 400, 404 for a non-owner, 409 with `currentRevision`, and 422 with `ringIndex`. The endpoint appears in the OpenAPI document. Use a derived factory fixture, not `WithWebHostBuilder` per test (memory).
- [x] T038 [P] [US1] Create `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/viewStateCapture.test.ts`, against a fake map and camera store:
  - entry disables rotation, then sets plan, heading 0 and `fitBounds`;
  - exit restores the mode, `moveCamera`, and rotation last;
  - all four combinations of 3D/plan and rotating/fixed come back exactly (SC-002);
  - reduced motion means no animated moves.
- [x] T039 [P] [US1] Create `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/editablePolygonController.test.ts`, against a fake `EditablePolygonHost`: `setRings` updates the paths without calling `applyChange` or `refuse`; undo after a move puts the corner back on the polygon.
  - one polygon per ring, with only the active ring editable;
  - a valid `set_at` goes to `applyChange`;
  - an invalid `set_at` is reverted without re-entering, and `refuse` is called;
  - `unmount` removes every listener and polygon.
- [x] T040 [P] [US1] Create `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryEditToolbar.test.tsx` and `SiteBoundaryEditToolbar.a11y.test.tsx`:
  - the area text;
  - Undo and Redo disabled on empty stacks;
  - Done disabled while unchanged or saving;
  - an error `Alert` with Retry;
  - jest-axe passes.

### Implementation for User Story 1: backend

- [x] T041 [US1] Create `SiteBoundaryEditOffer.Build(ActiveSiteBoundary, IReadOnlyList<SuggestedAction> analysisRows)` in `src/AskLucy.Application/Conversations/Runtime/SiteBoundaryEditOffer.cs` (research D6).
- [x] T042 [US1] Change the keep row in `src/AskLucy.Application/Conversations/Runtime/SiteBoundaryMembershipOffer.cs` to a real `set_site_boundary_members` row with the included ids and `keep: true`.
- [x] T043 [US1] Handle `keep` in `src/AskLucy.Application/Conversations/Capabilities/SetSiteBoundaryMembersCapability.cs`: when the chosen ids equal the included ids, return `{kept: true, outlineCovers, areaSquareMeters}` with no geometry. Add the `kept` sentence to `UsageGuidance`, and change `AcknowledgementTemplate` to "Now updating the site outline." Put a plain sentence field FIRST in the result data (for example `"outline": "The outline stays as it is"`); `UsageGuidance` is only a backup, because the narrator sees only the result data.
- [x] T044 [US1] Create `EditSiteBoundaryCapability` in `src/AskLucy.Application/Conversations/Capabilities/EditSiteBoundaryCapability.cs` with the properties and result JSON from contracts/edit-and-reset-capabilities.md. Register it in `src/AskLucy.Application/DependencyInjection.cs` next to `SetSiteBoundaryMembersCapability`. Owner check goes through `ChatOwnershipAuditor`.
- [x] T045 [US1] Add `SiteBoundaryEditCommand(Guid ChatId, Guid Revision)` and `ChatStreamChunk.SiteBoundaryEdit` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/ChatStreamChunk.cs`. Map it in `src/AskLucy.Application/Conversations/Runtime/StructuredPayloadExtractor.cs`, and write the `siteBoundaryEdit` SSE frame in `src/AskLucy.Web/Controllers/v1/AiController.cs`, next to `solarAnalysis` (contracts/site-boundary-edit-sse-event.md).
- [x] T046 [US1] In `EmitOfferIfDueAsync` in `src/AskLucy.Application/Conversations/Runtime/ConversationTurnOrchestrator.cs`, implement precedence rows 2–4. After the membership check, when the outline became final and isn't hand-edited, build the analysis rows with `offerGenerator.GenerateAsync` (after suppression, dropping its question) and yield `SiteBoundaryEditOffer`. The edit row is never suppressed.
- [x] T047 [US1] Create `SaveSiteBoundaryEditCommand`, `SaveSiteBoundaryEditCommandValidator` and `SaveSiteBoundaryEditCommandHandler` in `src/AskLucy.Application/Chats/Commands/SaveSiteBoundaryEdit/`. The handler: Owner check goes through `ChatOwnershipAuditor`. A stale revision throws `new ConcurrencyConflictException(message, currentRevision)`; geometry refusals throw `SiteBoundaryGeometryRejectedException`.
  - checks ownership via `ChatOwnershipGuard`;
  - reads the effective revision;
  - validates with `ISiteRingGeometry` (validity plus the drift rules from research D11);
  - computes `UnionArea`;
  - creates the correction (found snapshot from the chat) or `ReplaceRings` on the linked one, and links the chat;
  - appends the templated Assistant `Text` message;
  - commits in one `SaveChanges`;
  - returns the effective `ChatActiveBoundaryDto` and the message DTO.
- [x] T048 [US1] Add `PUT api/v1/chats/{chatId}/site-boundary` to `src/AskLucy.Web/Controllers/v1/ChatsController.cs`, under the `chat-endpoints` rate policy. Map `ConcurrencyConflictException` to a 409 with a `currentRevision` extension, and geometry refusals to a 422 with `ringIndex` and `reason`, via the global handler (`SiteBoundaryGeometryRejectedException` → 422, `ConcurrencyConflictException` → 409), with no try/catch in the controller. Document each status with `[ProducesResponseType]`.

### Implementation for User Story 1: frontend

- [x] T049 [P] [US1] Add `saveSiteBoundaryEdit(chatId, {expectedRevision, rings})` with a zod response schema and a TanStack mutation hook in `src/AskLucy.Web/ClientApp/src/features/chat/api/chatsApi.ts`. On success, update `activeSiteBoundaryStore`, append the message to the messages cache, and invalidate chat detail.
- [x] T050 [P] [US1] Parse the `siteBoundaryEdit` event in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` (and `aiApi.test.ts`). In `features/chat/hooks/useChatStream.ts`, ignore it for other chats, refetch detail on a revision mismatch, and call `enter`. A failure shows a snackbar (contracts/site-boundary-edit-sse-event.md).
- [x] T051 [US1] Add `setOutlineVisible(visible: boolean)` to `GoogleMapsGisLayerHandle` in `src/AskLucy.Web/ClientApp/src/viewer/layers/gis/GoogleMapsGisLayer.ts`. It hides the animated rings and the fallback polygon without stopping the frame loop. Extend `SiteBoundaryRenderer.test.ts` if the renderer gains a visibility flag.
- [x] T052 [US1] Create `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/viewStateCapture.ts`, with `captureViewState`, `enterPlanForEditing(rings)` and `restoreViewState(state)`, using `viewerEngineStore.setCamera`, `CAMERA_VIEW_MODE_TILT` and `cameraRestoreGuard` (research D2).
- [x] T053 [US1] Create `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/editablePolygonController.ts` behind an `EditablePolygonHost` interface. It covers mount, unmount and active-ring switching on click, and handles `set_at` (moves) with the validation, revert and apply path. Insert and delete are added in US2. Add `setRings(rings, activeRing)`, which replaces each polygon's path while events are suppressed so no change handler fires; `useSiteBoundaryEditMode` calls it after undo, redo, conflict Load latest and ring switching.
- [x] T054 [US1] Create `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryEditToolbar.tsx` as specified in contracts/edit-mode-viewer.md: title, "about N m²", Undo, Redo, Cancel, Done, a refusal line, "Saving…", and an error `Alert` with Retry. All buttons are at least 44×44 px.
- [x] T055 [US1] Create `src/AskLucy.Web/ClientApp/src/viewer/siteBoundaryEdit/useSiteBoundaryEditMode.ts`, wiring store, map and mutation. It covers the enter sequence, Done (save, then on success `setOutlineVisible(true)`, `setSiteBoundary(new)` for the animated redraw, and restore), Cancel (show the outline and restore), and save failure (keep the session open with an error, FR-018).
- [x] T056 [US1] Add an "Edit outline" control to `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryOverlay.tsx`, shown only when there is an outline, the viewer owns the chat and no session is open (FR-002). Mount the toolbar while a session exists. Extend `SiteBoundaryOverlay.test.tsx`.
- [x] T057 [US1] Disable the 3D/plan control in `src/AskLucy.Web/ClientApp/src/viewer/engine/MapRenderTarget.tsx` and `features/viewer/components/RotationToggleButton.tsx` while a session exists, with the tooltip "Finish editing the outline first" (FR-005). Extend their tests.
- [x] T058 [US1] Run the full frontend suite (ChatPage tests assert offers independently, memory) and fix `ChatPage.test.tsx` for the new offer rows and the keep row.

**Checkpoint**: US1 works alone: offer, enter, move, Done or Cancel, and the view restored. Walk
through quickstart steps 1, 2, 5 and 10.

---

## Phase 4: User Story 2 - Add and remove corners (Priority: P1)

**Goal**: Midpoint handles add corners. A context menu, long-press or the Delete key removes
them, with refusals below 3 corners or on self-crossing.

**Independent Test**: quickstart steps 3 and 4. Add a corner, drag it, delete another, then Done.
The saved ring has one corner added and one removed.

- [ ] T059 [P] [US2] Extend `editablePolygonController.test.ts` with these cases:
  - an `insert_at` becomes an insert change;
  - a delete on a 4-corner ring removes it;
  - a delete on a 3-corner ring is refused with "An outline needs at least 3 corners.";
  - an insert or delete that makes the ring cross itself is reverted;
  - undo reverts inserts and deletes in order.
- [x] T060 [US2] Handle `insert_at` and `remove_at` in `editablePolygonController.ts`, with the same validate, revert and apply path as moves. Add a `deleteCorner(ring, index)` that validates before removing.
- [ ] T061 [US2] Create `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryCornerMenu.tsx`: an MUI `Menu` anchored at the vertex's screen point, opened by the polygon's `contextmenu` event or a long-press (the `PolyMouseEvent.vertex` from T002). It has one item, "Delete corner", disabled with the explanation when the ring has 3 corners. Test it in `SiteBoundaryCornerMenu.test.tsx`.

**Checkpoint**: US1 and US2 together cover every pointer edit. Walk through quickstart steps
3 and 4.

---

## Phase 5: User Story 3 - The correction sticks, in every chat, and Lucy knows about it (Priority: P1)

**Goal**: The edit survives a reload, is reused for the same site in the user's other chats
without resolving, drives analyses, and Lucy reports the edited area.

**Independent Test**: quickstart steps 6, 7 and 11, and step 8 without its Reset-offer check (that offer arrives with US5).

- [x] T062 [P] [US3] Create `tests/AskLucy.Application.Tests/SiteBoundaries/SiteBoundaryCorrectionMatcherTests.cs`:
  - the same name and point inside the found rings grown by 100 m matches;
  - within 250 m of the centroid matches;
  - the same name 5 km away doesn't match;
  - a different user never matches;
  - normalised name variants match.
- [x] T063 [P] [US3] Extend `tests/AskLucy.Application.Tests/Conversations/Capabilities/ResolveSiteBoundaryCapabilityTests.cs`: Assert that the first field of the result data is that plain sentence.
  - a match links the correction, copies the found snapshot and returns `userCorrected: true`;
  - no Overpass, vision or membership calls are made (the substitutes receive nothing);
  - no match falls through to normal resolution.
- [x] T064 [P] [US3] Create `tests/AskLucy.Application.Tests/Conversations/Runtime/ActiveSiteNoteTests.cs`: the line for a found outline and the line for a hand-edited one (with "outline hand-edited by the user" and the area); no line without an outline.
- [x] T065 [P] [US3] Create `tests/AskLucy.Application.Tests/Conversations/Runtime/HandEditedOutlineFollowUpTurnsTests.cs`: run 10 turns through the orchestrator on a linked correction (3 area questions, 2 "show me the same site", 1 building choice, 2 analyses, 2 unrelated). Assert the edited area is in every turn's context and `ActiveSiteNote`, the found area is never current, no edit or membership offer is emitted, and the correction is never unlinked (SC-006).
- [x] T066 [US3] Create `SiteBoundaryCorrectionMatcher` in `src/AskLucy.Application/SiteBoundaries/SiteBoundaryCorrectionMatcher.cs` (research D5).
- [x] T067 [US3] In `src/AskLucy.Application/Conversations/Capabilities/ResolveSiteBoundaryCapability.cs`, look up a correction before resolving. On a match: `RecordActiveSiteBoundary` from the found snapshot, `LinkSiteBoundaryCorrection`, and return the effective payload plus `userCorrected: true`, with the `UsageGuidance` sentence. Make sure the membership offer and the edit offer both skip a hand-edited outline (FR-001, FR-023). Put `"outlineOrigin": "The user's own corrected outline, saved earlier"` FIRST in the result data; `UsageGuidance` is only a backup (the narrator sees only the result data).
- [x] T068 [US3] Create `ActiveSiteNote.Describe(TurnContext)` in `src/AskLucy.Application/Conversations/Runtime/ActiveSiteNote.cs`, and add its line to the fast-path reply prompt and the `TurnDecider` prompt (research D8, FR-021).
- [x] T069 [US3] Add the conflict UI to `SiteBoundaryEditToolbar.tsx` and `useSiteBoundaryEditMode.ts`: on a 409, show the "The outline changed in another tab." `Alert` with **Load latest** (refetch detail and re-enter from it, keeping the captured view state) and **Cancel** (FR-019). Extend the toolbar tests.
- [x] T070 [US3] Show a "Hand-edited" state in `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryConfidenceBadge.tsx` when `isHandEdited`, with the high-confidence style (spec Assumptions). Extend its test and a11y test.
- [x] T071 [P] [US3] Extend `tests/AskLucy.Web.Tests/Chats/SiteBoundaryEditEndpointTests.cs`: after a PUT, `GET /chats/{id}` returns the edited rings with `isHandEdited: true`; a second chat of the same user linked to the same correction sees the latest edit; another user's chat never does (FR-026). (Done at the layer that can run without a second signed-in user, as `OwnershipTests` documents: the save/GET/reuse rules are asserted in `SaveSiteBoundaryEditCommandTests`, `EffectiveSiteBoundaryTests`, `GetChatByIdQueryHandlerTests` and `ResolveSiteBoundaryReuseTests`; the endpoint tests cover the 401 gate and the OpenAPI contract. A two-user HTTP test needs seeded accounts the test host does not have.)

**Checkpoint**: P1 is complete. Walk through quickstart steps 6, 7, 8 and 11.

---

## Phase 6: User Story 4 - Change the building choice without losing the edits (Priority: P2)

**Goal**: The building choice on a hand-edited outline adds or removes ground around the edits
and keeps the hand-placed corners.

**Independent Test**: quickstart step 9.

- [ ] T072 [P] [US4] Extend `NtsSiteRingGeometryTests.cs` with join and cut cases:
  - `Join` of a square and a neighbour 1 m away gives one ring, with every corner more than 3 m from the seam unchanged (within 1e-6 m);
  - `Cut` of that footprint restores the original corners away from the seam, with no sliver thinner than 0.5 m;
  - a separate footprint stays a separate ring.
- [ ] T073 [US4] Implement `Join` and `Cut` in `src/AskLucy.Infrastructure/Boundaries/NtsSiteRingGeometry.cs`, using the seam-confined buffer from research D9.
- [ ] T074 [P] [US4] Extend `tests/AskLucy.Application.Tests/SiteBoundaries/SiteBoundaryMembershipServiceTests.cs` and `SetSiteBoundaryMembersCapabilityTests.cs` for hand-edited outlines: Assert that the first field of the result data is that plain sentence.
  - adding a separate member adds a ring;
  - adding a connected member joins it;
  - removing a member drops its ring or cuts it out;
  - the correction's rings, members and revision update, and its found snapshot is re-based;
  - the result has `handEdited: true`.
- [ ] T075 [US4] Add a hand-edited path to `src/AskLucy.Application/SiteBoundaries/SiteBoundaryMembershipService.cs`: diff the members against the correction, then join, cut, or add or remove rings. Keep the existing raster `Compose` for the found outline.
- [ ] T076 [US4] Use the effective outline in `src/AskLucy.Application/Conversations/Capabilities/SetSiteBoundaryMembersCapability.cs`. When it is hand-edited, apply the hand-edited path, then `correction.ApplyMembership` and recompose the chat's found outline. Return `handEdited: true` and add the `UsageGuidance` sentence (FR-022). Persist both in one `SaveChanges`. Put a plain sentence field FIRST in the result data (for example "The building choice was applied and the user's hand edits were kept"); `UsageGuidance` is only a backup.

**Checkpoint**: Walk through quickstart step 9.

---

## Phase 7: User Story 5 - Go back to what Lucy found (Priority: P2)

**Goal**: Reset from the map or by asking Lucy. The correction is deleted everywhere, and the
found outline is redrawn with effects.

**Independent Test**: quickstart step 12, plus step 8's Reset offer.

- [ ] T077 [P] [US5] Create `tests/AskLucy.Application.Tests/Chats/ResetSiteBoundaryCommandTests.cs`: owner only; a revision mismatch gives a conflict; not hand-edited gives not-found (FR-028); the correction is soft-deleted and the chat unlinked; one message "The outline of {site} is back to the one I found — {area:N0} m²"; one `SaveChanges`. A non-owner attempt writes one `AuthorizationDenied` audit row.
- [ ] T078 [P] [US5] Create `tests/AskLucy.Application.Tests/Conversations/Capabilities/ResetSiteBoundaryCapabilityTests.cs` and extend `ConversationTurnOrchestratorSiteBoundaryOfferTests.cs` with precedence row 1: a reused correction gives the reset offer, with the reset row first, then the analysis rows, then `Decline("Keep my outline")`.
- [ ] T079 [US5] Create `ResetSiteBoundaryCommand` and its handler in `src/AskLucy.Application/Chats/Commands/ResetSiteBoundary/`. Add `POST api/v1/chats/{chatId}/site-boundary/actions/reset` to `ChatsController.cs`, with the same rate policy, Problem Details mapping and OpenAPI attributes as T048. Owner check goes through `ChatOwnershipAuditor`.
- [ ] T080 [US5] Create `ResetSiteBoundaryCapability` in `src/AskLucy.Application/Conversations/Capabilities/ResetSiteBoundaryCapability.cs`, sending `ResetSiteBoundaryCommand` and returning the found `SiteBoundaryPayload` plus `resetFromHandEdit: true`, so the existing `siteBoundary` event redraws it. Register it. Add precedence row 1 to `EmitOfferIfDueAsync`. Owner check goes through `ChatOwnershipAuditor`.
- [ ] T081 [US5] Add `resetSiteBoundary` and its mutation to `chatsApi.ts`. In `SiteBoundaryOverlay.tsx`, add a "Reset to Lucy's outline" control, only when `isHandEdited`, with an MUI confirm dialog. On success, update the store, which redraws with the animated border. Failures show an inline `Alert`. Test it in `SiteBoundaryOverlay.test.tsx`, using `getByText` inside the dialog (memory: jsdom Dialog crash).
- [ ] T082 [P] [US5] Extend `SiteBoundaryEditEndpointTests.cs`: reset returns 200 with the found outline; another chat linked to the same correction reads the found outline afterwards (US5 AS2); resetting a never-edited outline gives 404; a stale revision gives 409.

**Checkpoint**: Walk through quickstart step 12.

---

## Phase 8: User Story 6 - Edit without a mouse (Priority: P3)

**Goal**: Keyboard and touch parity, meeting WCAG 2.1 AA.

**Independent Test**: quickstart step 13, plus the same edits on a touch screen.

- [ ] T083 [P] [US6] Create `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryCornerNavigator.test.tsx` and `.a11y.test.tsx`:
  - Tab and Shift+Tab roving, with Tab past the last corner leaving the region;
  - `[` and `]` switch rings;
  - the arrow keys move 0.5 m, and 5 m with Shift;
  - Insert or `+` adds a corner, Delete or Backspace removes one;
  - Ctrl+Z, Ctrl+Shift+Z and Ctrl+Y work;
  - Escape asks to confirm when there are unsaved changes;
  - the live-region text "Corner 3 of 12, …" is announced;
  - jest-axe passes.
- [ ] T084 [US6] Create `src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryCornerNavigator.tsx` (`role="application"`, `aria-roledescription="outline editor"`, a polite live region). It moves corners by offsetting in ENU metres via `ringGeometry.ts`, and routes every change through the store and controller so the same validation applies.
- [ ] T085 [US6] Draw the selected-corner highlight: one marker, not one per corner, updated from `selectedCorner` in `editablePolygonController.ts`. Keep `gestureHandling: 'greedy'` during edit mode, and check touch dragging on a real device (quickstart step 13).

**Checkpoint**: Every story works. Walk through quickstart steps 1–13.

---

## Phase 9: Polish and cross-cutting concerns

- [x] T086 Lifecycle, FR-030: in `useSiteBoundaryEditMode.ts`, when a `siteBoundary` event or chat detail brings a **different** site for the session's chat, call `forceExit`: restore the view and show the snackbar "Your unsaved outline changes were dropped because a new site was shown." Test it in `useSiteBoundaryEditMode.test.ts`. Also force the exit when the outline is cleared (`RecordActiveLocationCommandHandler` clears it when a different location is confirmed), and test that case.
- [ ] T087 Lifecycle, FR-029: selecting another chat while the session has changes opens an MUI dialog with Save, Discard and Stay. It is wired where chat selection navigates (the chat list or sidebar) through the store. Returning to `/studio` re-mounts the editable polygons from the store. Test both.
- [ ] T088 [P] Performance, SC-005: extend `ringGeometry.test.ts` and `editablePolygonController.test.ts` with a 500-corner ring, where a change is validated and applied in under 4 ms. Then check the drag on the RTX 4060 machine (quickstart step 15).
- [ ] T089 [P] Security review of the new endpoints and capabilities:
  - every correction query is filtered by `UserId`;
  - a non-owner gets 404, never 403;
  - request size is bounded by the validator;
  - no hand-edited geometry appears in logs at Information level or above.

  Record the result in spec.md "Verification".
- [ ] T090 Apply the migration to the **test2** database by hand before pushing (memory: Persistence.Tests uses test2), then run `dotnet test` for all five backend test projects and `npm test` in the ClientApp.
- [ ] T091 [P] Documentation (§13): add to `specs/079-site-boundary-manual-editing/spec.md`:
  - Decision, Behaviour changes, API, Database (migration notes) and Verification sections, in the style of specs/077;
  - a status line;
  - a pointer from specs/042's deferred-editing assumption to 079.

  Update the viewer README (`src/AskLucy.Web/ClientApp/src/viewer/README.md`) with the edit-mode folder.
- [ ] T092 Walk through quickstart.md steps 1–15 on localhost:7170 and then production, with screenshots judged against the expected values (memory: screenshot verification loop).

---

## Dependencies and execution order

```text
Phase 1 Setup ──▶ Phase 2 Foundational ──▶ US1 (P1, MVP) ──┬──▶ US2 (P1) ──▶ US6 (P3)
                                                           ├──▶ US3 (P1) ──┬──▶ US4 (P2)
                                                           │               └──▶ US5 (P2)
                                                           └──────────────────────────▶ Polish
```

- T002 (the spike) must finish before T053 and T061: it decides the event handling and the
  delete trigger.
- T018 (geometry port) comes before T047. T073 (join and cut) comes before T075.
- US5's reset offer (T080) needs US3's reuse (T067) to be reachable from chat. The map Reset
  control (T081) needs only T079.
- Within each story: tests first (they must fail), then backend, then frontend.

## Parallel opportunities

- **Phase 2**: T003, T004, T008 and T009 (Domain) run alongside T025, T026, T027 and T029
  (client), since they are different stacks. T014, T019, T023 and T024 run once their subjects
  exist.
- **US1**: tests T030–T040 are all [P]. Backend T041–T048 and frontend T049–T057 can run in two
  lanes, joined at T055.
- **After US1**: US2 (frontend only) and US3 (mostly backend) in parallel.
- **After US3**: US4 (geometry and backend) and US5 in parallel.

```bash
# US1 test lane, one batch:
Task: "SiteBoundaryEditOfferTests"                       # T030
Task: "SiteBoundaryMembershipOfferTests keep row"        # T031
Task: "EditSiteBoundaryCapabilityTests"                  # T033
Task: "SaveSiteBoundaryEditCommandTests"                 # T036
Task: "viewStateCapture.test.ts"                         # T038
Task: "editablePolygonController.test.ts"                # T039
```

## Implementation strategy

### MVP scope: Phases 1, 2 and 3 (US1)

Lucy offers the edit, the user moves corners, Done saves in this chat, and the view comes back
exactly. Moving existing corners alone fixes most near-miss outlines (spec US1 rationale). Stop
and validate against quickstart steps 1, 2, 5 and 10.

### Incremental delivery

US1 → US2 (full pointer editing) → US3 (all chats, Lucy knows): P1 is complete and shippable.
Then US4 (building choices keep edits) → US5 (reset) → US6 (keyboard and touch parity). Each
adds value without breaking the ones before it.

## Notes

- **Binding constraints that are easy to lose**:
  - the chat's own `ActiveBoundary` columns are **never** overwritten with edited rings; the
    edit lives only in the correction (research D5);
  - every outline reader goes through `EffectiveSiteBoundary` (T021), or the edit silently
    doesn't apply somewhere;
  - the editor uses 2D map polygons, never a new WebGL overlay (the 049–052 constraints).
- The Application layer never references EF Core. Concurrency surfaces as Domain
  `ConcurrencyConflictException` (memory).
- Stage files explicitly when committing. Never stage `appsettings.json`, `graphify-out/`,
  `.specify/feature.json` or `.codex/`.
