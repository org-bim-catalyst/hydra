# Implementation Plan: Site Boundary Resolution

**Branch**: `042-site-boundary-resolution` | **Date**: 2026-08-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/042-site-boundary-resolution/spec.md`

## Summary

Give Lucy a reusable capability to resolve a named/addressed site's geographic boundary as a polygon — with a stated confidence level and data source — and render it highlighted in the existing Three.js/Google Maps viewer. Technical approach: add a new `SiteBoundaries` module (Domain/Application/Infrastructure) that sits alongside and reuses `specs/037-location-query-resolution`'s point-resolution pipeline; source candidate polygons from OSM's free Overpass API scored by a config-driven deterministic weighted scorer (ported from `docs/AL_SAFA_PARK_2_AI_ANALYSIS_V5.ipynb`); render the result by extending `GoogleMapsGisLayer.ts`'s existing `THREE.Scene`/`WebGLOverlayView` bridge with a generalized, shader-only animated perimeter highlight (adapted from `docs/BORDER_HIGHLIGHT.html`, no post-processing pipeline).

**Corrected during Phase 0/1 research** (see research.md #10-11): the *primary* integration point is not an `IAgentTool` as the original request's wording suggested — it's a new deterministic pipeline stage inside `SendChatMessageCommandHandler`/`AiController`, mirroring `ILocationResolutionService`'s existing wiring exactly (`ConfirmedLocationData` → `RecordActiveLocationCommand` → `__LOCATION__` SSE event becomes `ConfirmedSiteBoundaryData` → `RecordActiveSiteBoundaryCommand` → `__SITE_BOUNDARY__`). This also means the boundary IS persisted — one small additive `ActiveSiteBoundary` owned-value column set on the existing `UserChats` table, mirroring `ActiveLocation` exactly, not the "no persistence" call the original architecture doc made. A `SiteBoundaryResolverTool : IAgentTool` is still built as a secondary surface for custom user-authored agents, wrapping the same underlying service. No tier gating (FR-014); AI-vision critique deferred to a later phase.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend — `AskLucy.Domain`/`Application`/`Infrastructure`/`Api`); TypeScript 5 / React 19 (frontend — `AskLucy.Web/ClientApp`). Matches the existing solution; no new runtime.

**Primary Dependencies**: Existing `ILocationResolutionService`/`IGeocodingProvider` (spec 037, reused not duplicated); existing `IAgentTool` contract + Agent Runtime (spec 020); `IHttpClientFactory` named-client pattern (mirrors `"Geocoding"`); `Microsoft.Extensions.Options` (`IOptions<T>`, `ValidateOnStart`); Google Maps JS API + the `WebGLOverlayView`/Three.js bridge already used by `GoogleMapsGisLayer.ts` (spec 027); Zustand (frontend state); MUI (confidence badge). **No new NuGet or npm package** — OSM Overpass is called via a plain named `HttpClient`, exactly like `NominatimGeocodingProvider`; no geometry/GIS library is added (custom `GeometryMath` helper instead, per architecture doc §7.3).

**Storage**: SQL Server — one small additive migration on the existing `UserChats` table (new nullable `ActiveSiteBoundary*` owned-type columns, mirroring `ActiveLocation*`'s columns from migration `20260823190247_AddActiveLocationToUserChat`). Not a new table, not a history/audit log — corrected from the original "N/A" call after confirming `ActiveSiteLocation` is itself a persisted column, not an in-memory-only value (research.md #10).

**Testing**: xUnit + FluentAssertions + Moq/NSubstitute for backend unit tests (`BoundaryCandidateScorer`, `BoundaryResolutionService` with a faked `IBoundaryCandidateProvider`); an `AskLucy.Infrastructure.Tests` integration test for `OverpassBoundaryCandidateProvider` against recorded/replayed HTTP responses (constitution §10); Vitest + React Testing Library for `activeSiteBoundaryStore` and `SiteBoundaryOverlay`/`SiteBoundaryRenderer`; a Playwright scenario extending existing map/location e2e coverage for the quickstart flow.

**Target Platform**: Existing ASP.NET Core Web API + React SPA (server-side agent/chat pipeline; browser-side Three.js/Google Maps viewer). No new deployment target.

**Project Type**: Web application — existing structure (`Domain`/`Application`/`Infrastructure`/`Api` + `ClientApp`). This feature adds files within the existing layout; no new project.

**Performance Goals**: SC-001 — a resolved boundary is displayed within 10 seconds end-to-end (point resolution + OSM candidate search + scoring + render), consistent with the existing chat response budget.

**Constraints**: No new geometry/GIS package; no `EffectComposer`/bloom pipeline added to the viewer's GIS render path (confirmed absent today — shader-only additive glow instead, architecture doc §9.3/§11); AI-vision critique explicitly deferred to a Phase 2 narrow `IBoundaryVisionAnalyzer` (not an `IAIProvider` widening); available to every authenticated user regardless of subscription tier (FR-014); manual polygon editing is out of scope for this feature (tracked separately per user memory, not built here).

**Scale/Scope**: One site resolved per invocation; result held only for the active conversation (no cross-session history, no persisted candidate log); candidate set bounded by `MaxCandidates` (default 10, mirroring the notebook's default) to keep the OSM query and scoring cost bounded per request.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle / Rule | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | PASS | `Domain/SiteBoundaries` has zero outward references; `Application/SiteBoundaries` depends only on Domain + its own abstractions (`IBoundaryCandidateProvider`); `Infrastructure/Boundaries` implements those abstractions and depends inward only. |
| II. SOLID | PASS | `IBoundaryCandidateProvider` is narrow/client-specific (mirrors `IGeocodingProvider`); `SiteBoundaryResolverTool` has one reason to change (adapt the service to `IAgentTool`); `IBoundaryVisionAnalyzer` is a separate, optional seam (OCP — added later without touching the resolver). |
| III. Simplicity — DRY/KISS/YAGNI | PASS | Reuses `ILocationResolutionService` rather than re-implementing geocoding; no new geometry library; persistence limited to mirroring `ActiveLocation`'s existing single-slot pattern (no new table, no history/audit log); no AI-vision step until a real need is proven (matches the notebook's own off-by-default posture). |
| IV. Composition over inheritance | PASS | No inheritance introduced; behavior composed via injected interfaces. |
| V. Dependency Inversion & Testability | PASS | `BoundaryResolutionService` depends on `IBoundaryCandidateProvider`/`ILocationResolutionService` interfaces, fully mockable; `IBoundaryVisionAnalyzer?` is an optional injected dependency (null = feature off), not a static/service-locator check. |
| VI. Separation of Concerns | PASS | `SiteBoundaryResolverTool` is a thin adapter (parses input, calls the service, serializes output) — no scoring/business logic in the tool class itself, matching `DocumentSearchTool`'s existing idiom. |
| VII. Convention over Configuration | PASS | Follows the `Locations` module's established convention exactly: a plain service class (not a MediatR command/query) invoked directly by its caller, a named `HttpClient` provider in `Infrastructure`, `IOptions<T>` config bound from a dedicated section. Introducing a parallel CQRS wrapper here — when `Locations` itself doesn't use one — would be the actual convention violation. |
| VIII. No Silent Failures | PASS | Every failure path (no candidates, ambiguous, provider unavailable) maps to an explicit, typed `BoundaryResolutionOutcome` the caller must handle and the user sees — never a caught-and-discarded exception, matching `LocationResolutionService`'s existing `Unavailable()` idiom. |
| CQRS rules (§3) | PASS | `IBoundaryResolutionService.ResolveAsync` itself is a plain service call from `SendChatMessageCommandHandler` (same precedent as `ILocationResolutionService` — not a new user-facing CRUD resource, no `IRequest` needed for the resolution step itself). The one piece of state-changing behavior — persisting the confirmed boundary — correctly IS a MediatR command (`RecordActiveSiteBoundaryCommand`), mirroring `RecordActiveLocationCommand` exactly; each has exactly one handler, and the command returns nothing beyond confirming the write. |
| Repository & Unit of Work | PASS | `RecordActiveSiteBoundaryCommandHandler` reuses the existing `IUserChatRepository`/`IUnitOfWork` — no new repository introduced, matching `RecordActiveLocationCommandHandler` exactly. |
| Infrastructure isolation | PASS | OSM Overpass is hidden entirely behind `IBoundaryCandidateProvider`; swapping to a future authoritative/cadastral source is an `Infrastructure` addition + DI change only. |
| AI Principles — provider/model abstraction | PASS | No `IAIProvider` change in this feature. The optional Phase-2 vision critique is scoped to its own narrow interface rather than widening the core text-only chat abstraction for all four providers. |
| AI Principles — Agent architecture | PASS | `SiteBoundaryResolverTool` (secondary surface, research.md #11) is a scoped, permissioned, read-only tool (RiskLevel: low) — no implicit capability beyond what its input schema grants. The primary chat-pipeline path is not an agent capability at all, so this gate applies only to the secondary tool. |
| Database Principles §5 | PASS | The new `UserChats` columns are nullable additive columns (no backfill needed, matching `ActiveLocation`'s own migration note); no new indexes needed (not queried by these columns, only read/written by primary key); migration is reversible (`Down` drops the added columns); no concurrency token needed beyond `UserChat`'s existing one, since this is a single-writer-per-request update like `ActiveLocation`. |
| Security §8 — Prompt injection | PASS | OSM tag/name data returned by the tool is untrusted external content; it flows back to the model only as validated, schema-shaped tool *output* (per the Agent Runtime's existing output-schema validation), never as raw instructions. |
| Security §6 — Rate limiting | PASS (existing pattern) | No new public endpoint is introduced — boundary resolution rides the existing, already-rate-limited chat-send endpoint (same as `ILocationResolutionService` today), plus the separate secondary `IAgentTool` path, already governed by the Agent Runtime's own bounding. The outbound Overpass call itself uses a timeout-bound named `HttpClient`, same posture as `NominatimGeocodingProvider`. |
| UI Principles §7 | PASS | Confidence badge/highlight uses the existing MUI theme and both light/dark themes; confidence is distinguished by more than color alone (icon/label text, not color-only) for WCAG 2.1 AA; client state lives in Zustand (`activeSiteBoundaryStore`), matching `activeLocationStore`. |
| Testing Standards §10 | PASS | Unit tests for Domain/Application with faked dependencies (no network/DB); an integration test for the one real Infrastructure call; a Playwright scenario for the end-to-end flow. |

No violations requiring justification — **Complexity Tracking is empty.**

**Post-Phase-1 re-check**: Design work surfaced two concrete changes not visible before research, both additive and backward-compatible, neither reopening a gate above:
1. `GoogleMapsGisLayerHandle` gains one new method, `setSiteBoundary()` (contracts/frontend-viewer-contract.md §3), because the lat/lng→scene-space `transformer` is frame-scoped and owned entirely inside `GoogleMapsGisLayer.ts`'s `onDraw`.
2. The primary invocation mechanism is a chat-pipeline hook, not `IAgentTool` (research.md #11) — corrected mid-design after reading `SendChatMessageCommandHandler.cs`/`AiController.cs`/`UserChat.cs` directly, rather than trusting the original architecture doc's assumption. This is exactly the kind of finding the Constitution Check gate exists to catch before code is written against a wrong integration point.

## Project Structure

### Documentation (this feature)

```text
specs/042-site-boundary-resolution/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   ├── chat-pipeline-integration.md
│   ├── site-boundary-resolver-tool.md
│   └── frontend-viewer-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Domain/SiteBoundaries/
├── GeoPoint.cs
├── SiteBoundaryPolygon.cs
├── SiteBoundarySource.cs
├── BoundaryConfidenceLevel.cs
└── SiteBoundaryResult.cs

src/AskLucy.Domain/Chats/
├── ActiveSiteBoundary.cs                # NEW — mirrors ActiveSiteLocation.cs
└── UserChat.cs                          # MODIFIED — adds ActiveBoundary property + SetActiveBoundary()

src/AskLucy.Application/SiteBoundaries/
├── IBoundaryCandidateProvider.cs
├── BoundaryCandidate.cs
├── ScoredBoundaryCandidate.cs
├── BoundaryScoringOptions.cs
├── BoundaryScoringOptionsValidator.cs
├── BoundaryCandidateScorer.cs
├── IBoundaryResolutionService.cs
├── BoundaryResolutionService.cs
├── BoundaryResolutionOutcome.cs
├── BoundaryConfirmationTemplates.cs     # mirrors LocationConfirmationTemplates.cs
├── BoundaryProviderUnavailableException.cs  # lives here, not Infrastructure — mirrors GeocodingProviderUnavailableException (Application must catch it without referencing Infrastructure)
└── GeometryMath.cs

src/AskLucy.Application/Ai/Commands/SendChatMessage/
├── ChatStreamChunk.cs                   # MODIFIED — adds ConfirmedSiteBoundaryData + ChatStreamChunk.ConfirmedBoundary
└── SendChatMessageCommandHandler.cs     # MODIFIED — launches boundary resolution alongside the existing location task

src/AskLucy.Application/Chats/Commands/RecordActiveSiteBoundary/
├── RecordActiveSiteBoundaryCommand.cs
└── RecordActiveSiteBoundaryCommandHandler.cs

src/AskLucy.Application/Agents/Tools/
└── SiteBoundaryResolverTool.cs          # secondary surface for custom agents (+ one DI registration line)

src/AskLucy.Infrastructure/Boundaries/
├── OverpassOptions.cs
└── OverpassBoundaryCandidateProvider.cs

src/AskLucy.Persistence/Configurations/
└── UserChatConfiguration.cs             # MODIFIED — adds OwnsOne(c => c.ActiveBoundary, ...) + polygon JSON value converter

src/AskLucy.Persistence/Migrations/
└── {timestamp}_AddActiveSiteBoundaryToUserChat.cs   # NEW migration (+ .Designer.cs, snapshot update)

src/AskLucy.Web/Controllers/v1/
└── AiController.cs                      # MODIFIED — accumulates ConfirmedBoundary, sends RecordActiveSiteBoundaryCommand, emits __SITE_BOUNDARY__

src/AskLucy.Web/ClientApp/src/features/chat/api/
└── aiApi.ts                             # MODIFIED — stream parser recognizes __SITE_BOUNDARY__ (mirrors __LOCATION__)

src/AskLucy.Web/ClientApp/src/store/
└── activeSiteBoundaryStore.ts

src/AskLucy.Web/ClientApp/src/viewer/layers/gis/
├── GoogleMapsGisLayer.ts                # MODIFIED — adds handle.setSiteBoundary() (research.md #8)
└── SiteBoundaryRenderer.ts              # NEW — builds the boundary Group's contents

src/AskLucy.Web/ClientApp/src/viewer/effects/
└── AnimatedBorderHighlight.ts

src/AskLucy.Web/ClientApp/src/features/viewer/components/
└── SiteBoundaryOverlay.tsx

tests/AskLucy.Domain.Tests/SiteBoundaries/
└── SiteBoundaryResultTests.cs

tests/AskLucy.Domain.Tests/Chats/
└── UserChatActiveBoundaryTests.cs       # NEW — SetActiveBoundary behavior, mirrors any existing SetActiveLocation test

tests/AskLucy.Application.Tests/SiteBoundaries/
├── BoundaryCandidateScorerTests.cs
└── BoundaryResolutionServiceTests.cs

tests/AskLucy.Application.Tests/Chats/Commands/RecordActiveSiteBoundary/
└── RecordActiveSiteBoundaryCommandHandlerTests.cs

tests/AskLucy.Application.Tests/Ai/Commands/SendChatMessage/
└── SendChatMessageCommandHandlerBoundaryTests.cs   # NEW test cases added to (or alongside) the existing handler test file

tests/AskLucy.Infrastructure.Tests/Boundaries/
└── OverpassBoundaryCandidateProviderTests.cs

tests/AskLucy.Persistence.Tests/
└── UserChatActiveBoundaryPersistenceTests.cs   # round-trips ActiveBoundary through a real/test SQL Server instance

src/AskLucy.Web/ClientApp/src/store/activeSiteBoundaryStore.test.ts
src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts        # extended — existing file, new __SITE_BOUNDARY__ cases
src/AskLucy.Web/ClientApp/src/viewer/effects/AnimatedBorderHighlight.test.ts
src/AskLucy.Web/ClientApp/src/features/viewer/components/SiteBoundaryOverlay.test.tsx
```

**Structure Decision**: Existing web-application layout (Option 2 shape, already in place as `Domain`/`Application`/`Infrastructure`/`Api` + `ClientApp`) is reused as-is. `SiteBoundaries` follows the exact flat-file-per-concept convention already established by `Locations` (Application) and `Workflows` (Domain); tests are co-located per project under `tests/AskLucy.<Layer>.Tests/SiteBoundaries/` (backend) and next to source under `src/.../*.test.ts(x)` (frontend), matching every existing module. No new backend or frontend project is created. The handful of *modified* (not new) files — `UserChat.cs`, `ChatStreamChunk.cs`, `SendChatMessageCommandHandler.cs`, `UserChatConfiguration.cs`, `AiController.cs`, `aiApi.ts` — are exactly the same files `specs/037-location-query-resolution` touched for `ActiveSiteLocation`, confirming this feature is additive to an established seam rather than a new integration pattern.

## Complexity Tracking

*No entries — Constitution Check passed with no violations.*
