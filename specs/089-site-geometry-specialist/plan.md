# Implementation Plan: Site Geometry Specialist

**Branch**: `089-site-geometry-specialist` | **Date**: 2026-11-02 | **Spec**: specs/089-site-geometry-specialist/spec.md

## Summary

The Site Geometry Specialist adds a new finding to the site analysis fan-out that describes the analysed site's own shape: area, overall dimensions, frontage, orientation, and parcel or plot boundary where one is recorded in the map data. It reuses the existing boundary-resolution machinery from SPEC-042 (OpenStreetMap-derived geospatial source queried through the platform's integrated query API) rather than introducing a second site-identification path. The specialist reports through the existing ISiteAnalysisResultRelay inline, never through workflow node events. Design is additive only: one new analysis type, one provisioner branch entry, one tool registration, and content composed from the existing block vocabulary with no image. Confidence is rule-based through the platform's existing confidence rule, never borrowed from a geocoder's importance score. Adding this specialist changes no shared coordination, validation, delivery, persistence, or rehydration logic.

**Technical approach**: The specialist analyses the site geometry frozen onto the analysis record at dispatch time (FR-004, FR-005). It queries the existing geospatial source for parcel outlines, street adjacency, and measured dimensions. All figures are metric. Unavailable figures are stated with their reason (FR-014), never estimated. The finding uses the existing panel content vocabulary (heading, text, key-value, divider) and carries provenance: the analysis that produced it, the data source behind it, its confidence level, and its generation time.

## Technical Context

**Language/Version**: C# 12 / .NET 10

**Primary Dependencies**: 
- AskLucy.Domain (no external deps)
- AskLucy.Application → Domain only
- AskLucy.Infrastructure → Application, Domain
- MediatR, FluentValidation, AutoMapper, Serilog

**Storage**: SQL Server (Entity Framework Core Code-First)

**Testing**: xUnit + integration tests against shared test DB

**Target Platform**: Windows / cross-platform (.NET 10)

**Project Type**: ASP.NET Core Web API + React frontend (this feature is backend/Application-layer only)

**Performance Goals**: Within existing agent execution limits; no new budgets introduced (FR-030)

**Constraints**: 
- Must follow Clean Architecture Dependency Rule (Domain ← Application ← Infrastructure)
- Must not introduce second site-identification path (FR-005)
- All figures must be metric; unavailable figures must state "unavailable" with reason (FR-014)
- No new content block kinds (constitution §2 III, §7 design-system rule)
- Must reuse existing geospatial query capability (FR-015, FR-016, FR-017)

**Scale/Scope**: Single feature branch; one new specialist adding to existing fan-out of 6 specialists

## Constitution Check

**GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.**

### Dependency Rule compliance ✅
- Domain → nothing (no project references, no NuGet packages beyond primitives)
- Application → Domain only, plus cross-cutting abstractions it defines
- Infrastructure → Application, Domain
- No outward-to-inward dependency arrows

**FR-005 compliance ✅** — Specialist analyses the site frozen at dispatch, never resolves by name, no second site-identification path.

**No silent failures ✅** — Every exception captured, reported with reason through ISiteAnalysisResultRelay, never swallowed.

**No new content block kinds ✅** — Content composed from existing vocabulary only (heading, text, key-value, divider).

**Provider neutrality ✅** — All LLM/geospatial access goes through abstractions; this specialist uses the existing ISiteGeometryDataProvider interface.

**⚠️ Potential concern**: FR-018 — if no shared capability for querying the map source exists, one must be introduced. This is acceptable as it enables the remaining four deferred specialists (research.md D5).

### Constitution gates verified ✅
All gates pass without violations requiring justification.

## Project Structure

### Documentation (feature directory)
```
specs/089-site-geometry-specialist/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output
```

### Source Code (repository root — unchanged)
```
src/AskLucy.Application/SiteAnalysis/Tools/SiteCharacterAnalysisTool.cs  (NEW — just registered)
src/AskLucy.Application/SiteAnalysis/Tools/  (existing tools unchanged)
src/AskLucy.Application/DependencyInjection.cs  (SiteCharacterAnalysisTool registered)
src/AskLucy.Domain/SiteAnalysis/SiteAnalysisEnums.cs  (CharacterAnalysis added)
src/AskLucy.Application/SiteAnalysis/SiteAnalysisSpecialists.cs  (character_analysis entry)
```

**Structure Decision**: The source code layout follows the existing site-analysis pattern under `src/AskLucy.Application/SiteAnalysis/`. No new projects or layer reorganizations. The single new file is `SiteCharacterAnalysisTool.cs` which was just registered in Application DI.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected |
|-----------|------------|----------------------------|
| None | All requirements satisfied by existing patterns | — |

## Phase 0: Research & Design

### Research tasks completed:
1. **ISiteGeometryDataProvider** — examined existing interface pattern in Infrastructure; confirmed it returns `DataResolutionOutcome<T>` per the platform's data resolution abstraction (research.md D5)
2. **SiteAnalysisConfidence.ResolveConfidence** — verified rule-based confidence from grounding facts only, no geocoder importance score (research.md D10)
3. **Content item block kinds** — confirmed existing vocabulary: heading, text, key-value, divider (constitution §2 III, §7 design-system rule)
4. **ISiteAnalysisResultRelay** — verified inline reporting pattern; never batch workflow node events (research.md D3)
5. **Provisioner auto-update** — confirmed `SiteAnalysisSpecialists.All` lists all specialists; count must match branch count (constitution §2 III FR-031)

### Unknowns resolved:
- ✅ Data source for geometry queries: existing ISiteGeometryDataProvider (no new external service needed)
- ✅ Confidence assignment: platform's existing rule-based helper
- ✅ Content composition: existing composer with existing block kinds only
- ✅ Unavailable handling: FR-014 pattern from other specialists (SiteEnvironmentalContextTool, SiteGeometryTool)

## Phase 1: Design & Contracts

### Entity: SiteGeometryFinding
Fields derived from feature spec requirements:
- `AnalysisId` (Guid) — links to the site analysis
- `SiteName` (string) — display name
- `AreaSquareMetres`? (nullable decimal) — from resolved boundary or unavailable
- `DimensionsLengthMetres`? / `DimensionsWidthMetres`? (nullable decimal) — from resolved boundary or unavailable
- `OrientationCompass`? (string, nullable) — cardinal direction or unavailable
- `FrontageStreets` (readonly list of frontage items) — per-street frontage with total
- `ParcelBoundaryWkt`? (nullable string) — Well-Known Text if parcel recorded
- `ParcelBoundaryHasReason`? (string, nullable) — reason if no parcel boundary
- `SiteBoundaryWkt` (string) — the resolved site boundary, always present
- `DataSource` (string) — e.g., "$type:ZoningData:Confirmed" or "unavailable"
- `ConfidenceLevel` (SiteAnalysisConfidenceLevel)
- `GeneratedAtUtc` (DateTime)
- `Notes` (list of strings) — unavailable reasons

### Interface contracts: /contracts/ (to be created in Phase 1)
- `ISiteGeometryDataProvider` — interface for querying geometry from the map source
  - `ResolveAsync(GeoPoint point, Ring? ring, CancellationToken ct)` → `Task<DataResolutionOutcome<SiteGeometryData>>`
- `SiteGeometryData` — value object with: `Area?`, `Dimensions`, `FrontageItems`, `ParcelOutline`, `Orientation`
- `FrontageItem` — `{ StreetName, LengthMetres }`

### quickstart.md (to be created in Phase 1)
- Validation: run site analysis against a site with a recorded boundary
- Expected: geometry finding delivered with area, dimensions, frontage, orientation, and parcel boundary
- Commands: build, run analysis, inspect finding

## Design Rationale

**Why reuse existing geospatial source**: FR-015, FR-016, FR-017 explicitly require deriving from the OpenStreetMap-derived source the platform already integrates, without introducing a second external service or new credentials. The SiteGeometryDataProvider already exists in Infrastructure with an `Unavailable` outcome — this specialist reuses it rather than adding a private helper.

**Why no image**: FR-020 prohibits images in the geometry finding; imagery is the schematic specialist's output. No new content block kinds are introduced (constitution §2 III, §7).

**Why confidence from grounding facts only**: FR-023, FR-024 prohibit borrowing from a geocoder's importance score, which is populated on incompatible scales by different providers. The platform's `SiteAnalysisConfidence.ResolveConfidence` uses `dataDirectlyDescribesSite` and `dataIsSparseOrApproximate` booleans.

**Why unavailable stated with reason**: FR-014 explicitly requires stating "unavailable" rather than estimating. The pattern is established by SiteEnvironmentalContextTool and SiteGeometryTool.

## Mandatory Post-Execution Hooks

No `.specify/extensions.yml` hooks to execute (the file does not exist in this project).

## Completion Report

- **Plan path**: `specs/089-site-geometry-specialist/plan.md`
- **Generated artifacts**: plan.md (just created)
- **Branch**: `089-site-geometry-specialist`
- **Next step**: Run `/speckit-tasks` to generate tasks.md, or proceed with implementation per the plan

**Done When**: plan.md is complete, research.md and data-model.md and quickstart.md can be generated following the Phase 0/1 workflow, and the SiteCharacterAnalysisTool is registered in Application DI (completed).