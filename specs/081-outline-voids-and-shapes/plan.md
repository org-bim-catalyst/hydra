# Implementation Plan: Voids and Drawing Shapes in the Outline Editor

**Branch**: `081-outline-voids-and-shapes` | **Date**: 2026-10-06 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/081-outline-voids-and-shapes/spec.md`

## Summary

Spec 079 refuses any cut that would leave a hole. This feature lets each part of a hand-corrected outline carry **voids** (atriums, courtyards), and adds **rectangle, square and free-polygon** drawing tools next to the circle, each able to Add or Cut.

Voids are stored as a list per part, alongside the existing outer edges. Nothing that already reads outer edges changes, and outlines saved before this feature need no rewrite. On the map, each part stays one native Google polygon, with its voids as inner paths, which Google draws as holes and lets the user edit. The server's geometry (NetTopologySuite) keeps the holes it currently refuses or drops, the area subtracts them, and building membership is unaffected. Shapes other than the circle are built on the client as polygons and sent to the existing combine endpoint, which now accepts a polygon as well as a circle.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript / React 19

**Primary Dependencies**: NetTopologySuite (Infrastructure only), MediatR, FluentValidation, EF Core; Google Maps JS (native editable `Polygon`), MUI, Zustand. No new packages.

**Storage**: SQL Server; a new JSON column `EditedVoidsJson` on `SiteBoundaryCorrections` (code-first migration, default `[]`)

**Testing**: xUnit (Domain, Application, Infrastructure, Persistence, Web); Vitest + Testing Library

**Target Platform**: Web (desktop, touch); the editor runs on the flat, north-up raster map

**Project Type**: Web application (Clean Architecture API + React SPA)

**Performance Goals**: a shape preview follows the pointer each frame; a combine round-trip stays as fast as today's circle (a single geometry operation)

**Constraints**: backward compatible with saved outlines and Lucy payloads; Google's Drawing Library is unavailable (deprecated August 2025, removed in the May 2026 Maps version); the existing corner limits include void corners

**Scale/Scope**: up to 20 parts, 50 voids per part, 5,000 corners in total

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design: still passes.*

| Principle | Assessment |
|---|---|
| I. Clean Architecture | Geometry stays behind `ISiteRingGeometry` (Application port, NTS in Infrastructure). The domain entity gains a value list. Controllers stay thin. **Pass** |
| II/III. SOLID, simplicity | Voids are added beside rings, with no new aggregate. The shape tools share one component (circle, rectangle, square) plus one for free polygons. No new endpoint: combine gains a polygon shape. **Pass** |
| V. Testability | Every geometry rule is tested at the port (Infrastructure tests) and in handlers with fakes. The client controller is tested with a fake host. **Pass** |
| VI. Separation of concerns | The client validates for immediate feedback; the server is authoritative for the combine result, the area and saving. **Pass** |
| VIII. No silent failures | Every refusal (void crossing the edge, voids touching, crossing polygon, nothing changed) is shown to the user. Server failures come back as 422 Problem Details with `ringIndex` and `voidIndex`. **Pass** |
| Section 5 Database | Additive migration with a default value; must be applied by hand to the test2 database before CI. **Pass, with a task** |
| Section 6 API | Additive fields on existing versioned endpoints; old clients keep working. **Pass** |
| Section 8 Security | Ownership checks (`ChatOwnershipAuditor`) unchanged; bounded request sizes (limits extended to voids); no geometry logged. **Pass** |

No violations.

## Project Structure

### Documentation (this feature)

```text
specs/081-outline-voids-and-shapes/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/site-boundary-voids-api.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/AskLucy.Domain/SiteBoundaries/SiteBoundaryCorrection.cs        # + EditedVoids
src/AskLucy.Domain/Chats/ActiveSiteBoundary.cs                      # + Voids (per ring index)
src/AskLucy.Domain/SiteBoundaries/SiteBoundaryGeometryRejectedException.cs  # + reasons, VoidIndex
src/AskLucy.Application/SiteBoundaries/ISiteRingGeometry.cs         # voids on UnionArea/Combine, ValidateVoids, NothingChanged
src/AskLucy.Application/SiteBoundaries/{SiteBoundaryPayload,HandEditedMembershipComposer,CorrectionOutline}.cs
src/AskLucy.Application/Chats/Commands/{CombineSiteBoundaryShape,SaveSiteBoundaryEdit}/
src/AskLucy.Application/Conversations/Runtime/ActiveSiteNote.cs
src/AskLucy.Application/Conversations/Capabilities/EditSiteBoundaryCapability.cs
src/AskLucy.Application/Chats/Queries/GetChatById/ChatDetailDto.cs  # ChatActiveBoundaryDto + Voids
src/AskLucy.Infrastructure/Boundaries/NtsSiteRingGeometry.cs
src/AskLucy.Persistence/Configurations/SiteBoundaryCorrectionConfiguration.cs + Migrations/<ts>_AddSiteBoundaryVoids.cs
src/AskLucy.Web/Contracts/ChatContracts.cs, Controllers/v1/{ChatsController,AiController}.cs, Middleware/ProblemDetailsMiddleware.cs

src/AskLucy.Web/ClientApp/src/
├── features/chat/api/{chatsApi,aiApi}.ts, features/chat/hooks/{useChatStream,useRestoreChatSite}.ts
├── store/activeSiteBoundaryStore.ts
├── viewer/siteBoundaryEdit/{siteBoundaryEditStore,ringGeometry,ringShapes,editablePolygonController,googleEditablePolygonHost,useSiteBoundaryEditMode,siteBoundaryEditActions}.ts
├── viewer/layers/gis/{GoogleMapsGisLayer,SiteBoundaryRenderer}.ts
├── features/viewer/components/{SiteBoundaryShapeDraw (was CircleDraw),SiteBoundaryPolygonDraw (new),SiteBoundaryShapeDialog,SiteBoundaryCornerNavigator,SiteBoundaryCornerMenu,SiteBoundaryBoxSelect,SiteBoundaryOverlay,SiteBoundaryEditHost}.tsx
└── features/chat/{OutlineActionGroup.tsx,outlineToolIcons.tsx}
```

**Structure Decision**: The existing layout. No new projects or folders: every change extends the spec 079 and 077 code where it already lives.

## Complexity Tracking

None.
