# Implementation Plan: Hand-Edit the Site Outline

**Branch**: `079-site-boundary-manual-editing` | **Date**: 2026-09-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/079-site-boundary-manual-editing/spec.md`

## Summary

Once an outline is final, Lucy offers to edit it. Accepting captures the map's view state, turns
the map to a north-up plan view, and swaps the animated outline for native editable
`google.maps.Polygon`s, one per ring (research D1, D2). The editor is client-only until Done.
It keeps its own undo stack, validates every change locally (self-crossing, fewer than 3 corners,
degenerate), and shows a running area.

Done `PUT`s the rings to a new chat sub-resource. The server validates again with
NetTopologySuite behind an Application interface (D3). It stores the edit as a per-user **site
correction** that every chat showing the same site links to (D5), and appends a templated chat
line (D8). It answers with the new outline, which the existing `SiteBoundaryRenderer` draws with
its animated border. The map then returns to the captured view.

The chat keeps its found outline untouched in its existing columns, so a reset just unlinks and
deletes the correction (D5).

Building choices on a hand-edited outline join or cut member footprints with exact vector
operations, leaving hand-placed corners away from the seam where they are (D3, D9).

A revision token on the outline in force refuses stale saves from a second tab (D4). Lucy learns
about the edit in three ways:

- the effective outline flows through `TurnContext.ActiveBoundary`;
- a one-line site note goes into the reply and decide prompts;
- the persisted chat line.

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 6.0 strict, React 19 (frontend)

**Primary Dependencies**: Existing:

- Backend: MediatR, FluentValidation, EF Core (SQL Server).
- Frontend: Maps JS API through `@googlemaps/js-api-loader` (the `google.maps.Polygon` editing
  built into the maps library, no drawing library), Zustand 5, MUI 9, TanStack Query, zod,
  Three.js 0.185 (the existing outline renderer, unchanged).

New: **NetTopologySuite** in `AskLucy.Infrastructure` only, behind `ISiteRingGeometry` (D3,
Complexity Tracking). No new frontend dependency: client-side validation and area math are
around 150 lines of plain TypeScript (D1).

**Storage**: SQL Server:

- New table `SiteBoundaryCorrections`.
- `UserChats` gains `ActiveBoundaryRevision` and `ActiveBoundaryCorrectionId`.
- One reversible migration.

See [data-model.md](data-model.md).

**Testing**:

- Frontend: Vitest, Testing Library, jest-axe. Type-check with `npx tsc -b --noEmit` (bare
  `--noEmit` checks nothing here).
- Backend: xUnit, NSubstitute, FluentAssertions. Web.Tests needs `PERSISTENCE_TESTS_CONNECTION_STRING`.
  Persistence.Tests uses the test2 database, which must be migrated by hand.

**Target Platform**: The Studio page in desktop and touch browsers, on the vector Google Map
with heading and tilt. Backend on the site4now IIS host.

**Project Type**: Web application (ASP.NET Core API with a React SPA in `src/AskLucy.Web/ClientApp`).

**Performance Goals**:

- Dragging a corner on a 500-corner ring keeps up with the pointer on the RTX 4060 reference
  machine (SC-005).
- Local validation is O(n) per change: only the edges next to the changed corner are tested
  against the ring.
- Save round trip under 1 s at p95. No network or AI call on the save path.

**Constraints**:

- No silent failures: every save, reset or conflict failure reaches visible UI (§2 VIII).
- The Application layer never references EF Core.
- The editor must not add a second WebGL overlay or touch renderer state (viewer constraints
  from the 049–052 plan). It uses the map's own 2D polygons.
- Workspace state survives navigation: the edit session lives in a module-level Zustand store.

**Scale/Scope**:

- At most 2,000 corners per ring and 20 rings per outline (FR-020 bounds).
- About one correction per user per site.
- Frontend: 1 new store, 1 map controller, 3 components.
- Backend: 2 capabilities, 1 command pair plus 1 query change, 1 entity, 1 geometry service.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How the design satisfies it |
|-----------|--------|-----------------------------|
| §2 I Clean Architecture | ✅ | `SiteBoundaryCorrection` lives in Domain. Commands, the `ISiteRingGeometry` port and the repository port live in Application. NTS and EF live in Infrastructure/Persistence. The controller only sends MediatR commands. |
| §2 III Simplicity (YAGNI) | ✅ with one justified dependency | Editing uses the map's built-in polygon editing, not a drawing library. NTS is the only addition (Complexity Tracking). No snapping, no whole-ring add or remove, no editing by words (spec Assumptions). |
| §2 V Testability | ✅ | Geometry sits behind `ISiteRingGeometry`. The map controller sits behind a thin `EditablePolygonHost` interface, so edit-session logic is unit-tested without Maps JS. |
| §2 VIII No Silent Failures | ✅ | Save or reset failures keep edit mode open with an inline error and Retry (FR-018). A 409 shows a conflict banner with "Load latest". A refused local edit shows a snackbar saying why. Server failures come back as Problem Details and are logged. |
| §3 Module boundaries | ✅ | Chat outline and correction logic stays in the Chat/Site Boundary area. Capabilities reach it through Application services, not repositories of other modules. |
| §5 Database | ✅ | Guid v7 key, audit columns via the interceptor, soft delete with a global filter, and `RowVersion` on `SiteBoundaryCorrections`. Index `(UserId, NormalizedSiteName)` filtered on `DeletedAtUtc IS NULL` covers the lookup path. `ActiveBoundaryCorrectionId` is indexed (no database foreign key). Reversible `Down`. Concurrency: a revision mismatch or `ConcurrencyConflictException` becomes 409 in Application (D4). |
| §6 API | ✅ | `PUT /api/v1/chats/{chatId}/site-boundary` (replace the outline) and `POST .../site-boundary/actions/reset`. `[Authorize]`, ownership checked in the Application handler, `chat-endpoints` rate limit, Problem Details (400/404/409/422), documented in OpenAPI. |
| §7 UI | ✅ | MUI toolbar and dialogs. Zustand for the edit session (UI state). TanStack Query mutations for save and reset. WCAG 2.1 AA: keyboard corner navigation, focus-visible, live announcements, reduced motion honoured (D2, D10). |
| §8 Security | ✅ | Owner-only writes (FR-020). Correction lookups are always scoped by `UserId` (FR-026). Server-side validation of every ring: count, coordinate range, simplicity, drift bounds. Non-owner denials on the new endpoints and capabilities are written to the role audit trail through `ChatOwnershipAuditor` (existing chat endpoints share the gap and are out of scope). No new secrets. Every user-supplied string is bounded. |
| §9 AI | ✅ | The edit and reset capabilities are Brief (no model or network call beyond narration). The save line is templated, not generated. Narration is shaped by result JSON only (memory: capability narrator). |
| §10 Testing | ✅ | Unit: geometry, validation, correction lookup, offer composition, edit-session store. Integration: endpoint 200/400/404/409 in Web.Tests. Persistence: migration and filters. Frontend: store, toolbar, a11y (jest-axe), ChatPage offer handling (full suite, see memory). |
| §13 Documentation | ✅ | Spec README-style "Decision / API / Database / Verification" sections added to spec.md on ship (as specs/077 did). API documented in OpenAPI. Migration note in data-model.md. |

**Gate result (pre-research)**: PASS. One new dependency, justified below.

**Re-check after Phase 1 design**: PASS.

- No new projects or controllers: the endpoints go on the existing `ChatsController`.
- The correction entity is its own aggregate. Chats reference it by id only, with no navigation,
  so there is no cross-aggregate `DbSet` access (§5).
- The design adds one SSE event kind (`siteBoundaryEdit`), following the existing
  `StructuredPayloadExtractor` pattern. No new transport.

## Project Structure

### Documentation (this feature)

```text
specs/079-site-boundary-manual-editing/
├── plan.md              # This file
├── research.md          # Phase 0: decisions D1–D11
├── data-model.md        # Phase 1: entities, columns, client store, migration notes
├── quickstart.md        # Phase 1: end-to-end validation run
├── contracts/
│   ├── site-boundary-edit-api.md        # PUT outline, POST reset, ChatDetailDto additions
│   ├── edit-and-reset-capabilities.md   # edit_site_boundary / reset_site_boundary + offers
│   ├── site-boundary-edit-sse-event.md  # siteBoundaryEdit stream event
│   └── edit-mode-viewer.md              # client edit-mode contract (view state, controls, keys)
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks (not created here)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/
├── SiteBoundaries/SiteBoundarySource.cs                  # + UserCorrected
├── SiteBoundaries/SiteBoundaryCorrection.cs              # NEW aggregate (per-user correction)
└── Chats/ActiveSiteBoundary.cs, UserChat.cs              # + Revision, CorrectionId, WithCorrection(...)

src/AskLucy.Application/
├── SiteBoundaries/ISiteRingGeometry.cs                   # NEW port: validate, union area, join, cut
├── SiteBoundaries/ISiteBoundaryCorrectionRepository.cs   # NEW port
├── SiteBoundaries/SiteBoundaryCorrectionMatcher.cs       # NEW: same user + name + place (D5)
├── SiteBoundaries/EffectiveSiteBoundary.cs               # NEW: found outline + live correction → outline in force
├── SiteBoundaries/SiteBoundaryMembershipService.cs       # Compose on hand-edited rings → join/cut (D9)
├── Chats/Commands/SaveSiteBoundaryEdit/                  # NEW command + validator + handler
├── Chats/Commands/ResetSiteBoundary/                     # NEW command + handler (shared with capability)
├── Chats/Queries/GetChatById/ChatDetailDto.cs            # activeBoundary: + revision, isHandEdited
├── Conversations/Capabilities/EditSiteBoundaryCapability.cs    # NEW
├── Conversations/Capabilities/ResetSiteBoundaryCapability.cs   # NEW
├── Conversations/Capabilities/ResolveSiteBoundaryCapability.cs # correction lookup before resolving (D5)
├── Conversations/Capabilities/SetSiteBoundaryMembersCapability.cs # "keep" row + hand-edited compose
├── Conversations/Runtime/SiteBoundaryEditOffer.cs        # NEW: edit row + analysis rows (D6)
├── Conversations/Runtime/SiteBoundaryMembershipOffer.cs  # keep row becomes a real row (D6)
├── Conversations/Runtime/ActiveSiteNote.cs               # NEW: one-line site note for prompts (D8)
├── Conversations/Runtime/ConversationTurnOrchestrator.cs # emit edit offer after the outline is final
└── Conversations/Runtime/StructuredPayloadExtractor.cs   # edit_site_boundary → SiteBoundaryEditCommand

src/AskLucy.Infrastructure/Boundaries/NtsSiteRingGeometry.cs    # NEW (NetTopologySuite)
src/AskLucy.Persistence/
├── Configurations/SiteBoundaryCorrectionConfiguration.cs # NEW
├── Configurations/UserChatConfiguration.cs               # + 2 owned columns + index
├── Repositories/SiteBoundaryCorrectionRepository.cs      # NEW
└── Migrations/<ts>_AddSiteBoundaryCorrections.cs         # NEW
src/AskLucy.Web/Controllers/v1/ChatsController.cs         # + PUT site-boundary, POST reset

src/AskLucy.Web/ClientApp/src/
├── viewer/siteBoundaryEdit/
│   ├── siteBoundaryEditStore.ts          # NEW Zustand: session, history, view state, status
│   ├── ringGeometry.ts                   # NEW: local ENU area, self-crossing, degenerate checks
│   ├── editablePolygonController.ts      # NEW: native editable Polygons ⇄ store, per ring
│   ├── viewStateCapture.ts               # NEW: capture / enter plan / restore (uses cameraRestoreGuard)
│   └── useSiteBoundaryEditMode.ts        # NEW hook wiring store, map, SSE command, mutations
├── features/viewer/components/
│   ├── SiteBoundaryEditToolbar.tsx       # NEW: Done / Cancel / Undo / Redo / area / errors
│   ├── SiteBoundaryCornerNavigator.tsx   # NEW: keyboard corner selection + live region
│   ├── SiteBoundaryOverlay.tsx           # + "Edit outline" / "Reset to Lucy's outline" controls
│   └── RotationToggleButton.tsx (+ 3D/plan control)  # disabled while editing
├── store/activeSiteBoundaryStore.ts      # + revision, isHandEdited
├── features/chat/api/chatsApi.ts         # + saveSiteBoundaryEdit, resetSiteBoundary
└── features/chat/hooks/useChatStream.ts  # + siteBoundaryEdit event

tests/
├── AskLucy.Domain.Tests/        # SiteBoundaryCorrection, ActiveSiteBoundary.WithCorrection
├── AskLucy.Application.Tests/   # save/reset handlers, validator, matcher, offers, capabilities, compose on edited rings
├── AskLucy.Infrastructure.Tests/# NtsSiteRingGeometry (validity, union area, join/cut keeps far corners)
├── AskLucy.Persistence.Tests/   # correction config, filtered index, migration
└── AskLucy.Web.Tests/           # endpoint 200/400/403-as-404/409, OpenAPI presence
```

**Structure Decision**: Existing Clean Architecture layout. Frontend edit-mode code goes in a new
`viewer/siteBoundaryEdit/` folder beside `viewer/camera/`. It is a viewer concern, not a GIS
layer concern, and `GoogleMapsGisLayer` only gains a `setOutlineVisible(boolean)` handle method
so the animated rings hide while the editable polygons show.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New NuGet dependency: NetTopologySuite (Infrastructure only) | FR-022 needs exact polygon union and difference that keep hand-placed corners. FR-016 needs a union area. FR-020 needs server-side simplicity checks. | The existing 0.5 m raster union (`RasterSiteFootprintUnion`) re-traces every corner, destroying the edits. Handwritten vector clipping (Greiner–Hormann) is fragile on shared walls and collinear edges, which are exactly the specs/077 case. NTS is pure managed code (no native DLL, unlike the PDFium collision) and BSD-licensed. |
