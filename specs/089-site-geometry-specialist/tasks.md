# Tasks: Site Geometry Specialist

**Input**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Test tasks included where the spec provides test coverage patterns; optional tasks marked [P] can run in parallel.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Initialize .NET 10 project structure per implementation plan (specs/089-site-geometry-specialist/plan.md)
- [ ] T002 Configure Solver for .NET 10 nullable reference types (`<Nullable>enable</Nullable>`) solution-wide
- [ ] T003 [P] Verify backend build passes with zero errors and zero new compiler warnings on changed files
- [ ] T004 [P] Run frontend lint (ESLint/Prettier) with no violations on changed files

---

## Phase 2: Foundational (Blocking Prerequisites for All User Stories)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

- [ ] T005 Create `OverpassTagFilter` record in `src/AskLucy.Application/Overpass/OverpassFeatureQuery.cs` — per data-model.md § 1: `OverpassTagFilter(string Key, IReadOnlyList<string>? Values, bool IncludeRelations = false)`
- [ ] T006 Create `OverpassFeatureQuery` record in same file — per data-model.md § 1: `OverpassFeatureQuery(GeoPoint Center, int RadiusMeters, IReadOnlyList<OverpassTagFilter> Filters)`
- [ ] T007 Create `OverpassFeature` record in `src/AskLucy.Application/Overpass/` — per data-model.md § 1: `OverpassFeature(string Id, string ElementType, IReadOnlyDictionary<string, string> Tags, IReadOnlyList<GeoPoint> Geometry)`
- [ ] T008 Create `SiteGeometryFeatureData` record in `src/AskLucy.Application/SiteAnalysis/Providers/SiteGeometryFeatureData.cs` — per data-model.md § 3: `SiteGeometryParcel`, `SiteGeometryStreet`, and `SiteGeometryFeatureData` with Parcels/Streets
- [ ] T009 Create `SiteGeometryMeasurements` record in `src/AskLucy.Application/SiteAnalysis/SiteGeometry/SiteGeometryMeasurements.cs` — per data-model.md § 4: `AreaSquareMeters`, `LengthMeters`, `WidthMeters`, `OrientationBearingDegrees`, `Frontages`, `LooksLikeResolvedBuffer`; constants `FrontageToleranceMeters = 25`, `MinimumQueryRadiusMeters = 60`, `MaximumQueryRadiusMeters = 1_500`, `PointOnlyQueryRadiusMeters = 120`, `NearSquareRatio = 0.05`, `RadiusMarginMeters = 80`
- [ ] T010 Create `GeometryMath` static class (or reuse existing) with `AreaSquareMeters`, `ToLocalMeters`, `Contains` extension methods per calculation rules
- [ ] T011 Create `ISiteGeometryDataProvider` interface in `src/AskLucy.Application/SiteAnalysis/Providers/` — `Task<DataResolutionOutcome<SiteGeometryFeatureData>> ResolveAsync(GeoPoint center, Ring? ring, CancellationToken ct)`; `DataResolutionOutcome<T>` is the existing vocabulary (Confirmed/Unavailable)
- [ ] T012 [P] Implement `OverpassSiteGeometryDataProvider` in `src/AskLucy.Infrastructure/Overpass/OverpassSiteGeometryDataProvider.cs` — reuses existing `OverpassBoundaryCandidateProvider` transport, adds parcel containment (`GeometryMath.Contains`), street attribution (edge→nearest named street ≤ 25 m), and orientation/dimensions projection
- [ ] T013 [P] Register `ISiteGeometryDataProvider → SiteGeometryDataProvider` in Infrastructure DI (justice per existing provider registration pattern; no `sp => sp.GetRequiredService<T>()` factory)
- [ ] T014 Create `SiteAnalysisContentComposer` verification — verify `ComposeSections` emits only `heading`, `text`, `divider`, `keyValue` blocks (no image, no table, no new kind) per research.md D8 and constitution §2 III, §7

---

## Phase 3: User Story 1 — The site's own geometry arrives as a finding (Priority: P1) 🎯 MVP

**Goal**: A user asks Lucy to analyze a site, and a second finding describes the site's own shape: area, overall dimensions, frontage, and orientation. The user can read the numbers and see where they came from, without asking a follow-up.

**Independent Test**: Run a site analysis against a site whose boundary the platform can resolve, and confirm a geometry finding is delivered in its own right — with area, dimensions, frontage, orientation, and a boundary — and that it is delivered as its own notice and panel rather than folded into the schematic map.

- [ ] T015 [US1] Create `SiteGeometryTool` in `src/AskLucy.Application/SiteAnalysis/Tools/SiteGeometryTool.cs` — implements `IAgentTool`, takes `{siteAnalysisId, siteName, siteLocation}`, queries `ISiteGeometryDataProvider`, reports metadata with zoning data figures and confidence level, with unavailable handling per FR-014. Follow the exact shape of existing `SiteCharacterAnalysisTool` and `SiteGeometryTool` patterns.
- [ ] T016 [US1] Register `IAgentTool, SiteGeometryTool` in Application DI `DependencyInjection.cs` — direct registration, never `sp => sp.GetRequiredService<T>()` (constitution §3, research.md D2 deadlock note)
- [ ] T017 [US1] Add `site_analysis_site_geometry` entry to `SiteAnalysisSpecialists.All` in `SiteAnalysisSpecialists.cs` — provisioner auto-updates from this list; count must match branch count
- [ ] T018 [US1] Write `SiteGeometryToolTests` in `tests/AskLucy.Application.Tests/SiteAnalysis/SiteGeometryToolTests.cs` — mirror `EnvironmentalContextToolTests` (substituted `ISiteAnalysisRepository` + `ISiteGeometryDataProvider` + relay, content-kind assertion); cover: parcel found → High, no parcel → Medium with the boundary stated, no boundary → figures unavailable, provider unavailable → success with unavailable figures, missing analysis → `ReportFailureAsync` + failure, missing input id → failure without a relay call. Mirror `OverpassBoundaryCandidateProviderTests` stub handler pattern.
- [ ] T019 [US1] Write `SiteGeometryCalculatorTests` in `tests/AskLucy.Application.Tests/SiteAnalysis/` — covers area/dimensions/orientation (including a rotated rectangle and the near-square rule), frontage attribution and tolerance, and the regular-buffer detection (24-vertex, equidistant ring)
- [ ] T020 [US1] Write `OverpassSiteGeometryDataProviderTests` in `tests/AskLucy.Infrastructure.Tests/Overpass/` — covers parcel containment/smallest-area selection, split of streets from parcels, and the no-candidate outcome
- [ ] T021 [US1] Verify `OverpassBoundaryCandidateProviderTests` stays **unchanged** as the regression guard for the transport refactor (constitution §10: tests written for new behavior in the same PR that introduces it)
- [ ] T022 [US1] Build and run test suite: `dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~SiteAnalysis"` and `dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~Overpass|FullyQualifiedName~SiteAnalysis|FullyQualifiedName~Boundaries"` — all pass

---

## Phase 4: User Story 2 — Every geometry figure says where it came from (Priority: P1)

**Goal**: A user who is about to act on a measurement — a site area, a frontage length, a facing direction — must be able to see whether it is a measured figure from map data, a figure derived from the site's resolved shape, or a figure that could not be established. The finding states which, every time, and never presents an approximation in the place of a figure it does not have.

**Independent Test**: Open a delivered geometry finding and confirm it names the analysis that produced it, the data source behind it, and a confidence level, and confirm that any figure without a source is marked unavailable with a reason rather than estimated.

- [ ] T023 [US2] Implement FR-008–FR-013 in `SiteGeometryTool.ExecuteAsync` — every figure states its grounding; unavailable figures are explicit with reason; no approximation presented as measured; orientation reported as compass direction + bearing per FR-013; FR-014 unavailable text states "unavailable" rather than estimate
- [ ] T024 [US2] Implement data source string per `BuildMetadata` in `SiteGeometryTool` — `"$type:SiteGeometryFeatureData:Confirmed"` when confirmed, `"unavailable"` when not; per FR-014 and data-model.md § 5: build concise data-source string from zoning fields that are actually set (FAR, setback, height limit); if no fields set, `"unavailable"`
- [ ] T025 [US2] Implement confidence assignment per `SiteAnalysisConfidence.ResolveConfidence` — `dataDirectlyDescribesSite: true/false, dataIsSparseOrApproximate: ...` per research.md D7 table; parcel outline found and measured → High; figures measured from resolved site boundary → Medium; no boundary at all → Low; never from geocoder's importance score
- [ ] T026 [US2] Implement FR-011 frontage unavailable with reason — when street adjacency cannot be established from the source, frontage reported as unavailable with reason "no named street within 25 m of the site boundary"; FR-011: frontage MUST NOT be approximated from the site's longest edge
- [ ] T027 [US2] Implement FR-012 no boundary → every shape-dependent figure unavailable with reason — when the site has no resolved boundary, report every shape-dependent figure as unavailable with the reason; FR-012: the system MUST NOT report a shape it does not have
- [ ] T027 [US2] Write `SiteAnalysisResultMetadata` verification — confirms data source and confidence are set correctly for all outcome combinations (Confirmed/Unavailable, with/without Value); verdict set when verify pass runs
- [ ] T028 [US2] Verify finding content uses only existing block kinds — only `heading`, `text`, `divider`, `keyValue`; no `image`, no `table`, no new kind; per FR-019 and constitution §2 III, §7 design-system rule

---

## Phase 5: User Story 3 — A geometry specialist that fails does not sink the analysis (Priority: P2)

**Goal**: The map source is a free, shared, frequently-saturated service. When it is slow, rate-limited, or returns something unusable, the figures that depend on it are reported unavailable with the reason — while the figures measured from the site's already-resolved boundary are still delivered, the other findings still arrive, and the analysis still reaches a definite end state. A genuine fault — the analysis record cannot be read, or something unexpected fails — is recorded in full and diagnosed from the record or the logs alone.

**Independent Test**: Make the map source fail during an analysis, and confirm the geometry finding still arrives with its boundary-derived figures, its source-dependent figures marked unavailable, the other finding still arrives, the analysis still completed, and nothing interrupting the user.

- [ ] T029 [US3] Implement FR-025–FR-029 failure behaviour in `SiteGeometryTool` — genuine fault captured with cause and written to structured logs; map source slow/unavailable/rate-limited is NOT fatal: source-dependent figures reported unavailable with reason, boundary-derived figures still delivered, confidence capped at Medium; finding still reported as Success; analysis still reaches completed state; FR-027: failure MUST NOT prevent other specialists' findings from being delivered; FR-028: failure MUST NOT produce a notice, panel, or any other interruption for the user on its own; FR-029: system MUST NOT introduce a new kind of failure; reuse existing failure vocabulary and classification
- [ ] T030 [US3] Write progressive delivery tests — `SiteAnalysisProgressiveDeliveryTests` confirms each specialist's finding arrives independently; geometry finding arrives at its own time; when one specialist fails, the other still delivers and the analysis completes; verify `SiteAnalysisSpecialists.Count` gates the analysis closure (SC-003, SC-005)
- [ ] T031 [US3] Verify `SiteAnalysisSpecialists.All.Count == 6` after adding geometry specialist — the analysis's expected result count increases from 2 to 6, and the analysis completes only once all 6 specialists have settled (SC-008: 0 changes to coordinator, validation gate, delivery, persistence, rehydration)
- [ ] T032 [US3] Run full test suite under map-source-unavailable conditions — boundary-derived figures delivered, source-dependent figures marked unavailable, other findings still delivered, analysis completes, no error notice for geometry specialist on its own (SC-001–SC-003, SC-005–SC-007)

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T033 Verify `SiteAnalysisSpecialists.Count` remains stable: `All.Count` derives from the single `IReadOnlyList<SiteAnalysisSpecialist> All = [...]` declaration; adding a further specialist later adds one entry here, one IAgentTool, and one provisioner branch entry — no other structural change (FR-031)
- [ ] T034 Run `dotnet build` solution-wide: zero errors, zero new compiler warnings on changed files; verify all MediatR pipeline behaviors (ValidationBehavior, LoggingBehavior, AdminViewAuditBehavior) still pass
- [ ] T035 Run `dotnet test` across the full test suite — unit tests pass (≥80% line coverage floor for Domain and Application layers per constitution §10); integration tests pass in CI against shared test DB
- [ ] T036 Write a brief `quickstart.md` validation checklist — run scenarios 2–6 from quickstart.md and confirm each produces the expected findings; verify provenance block is last and shows analysis type, data source, confidence and generation time
- [ ] T037 Architecture compliance review — verify no Dependency Rule violations (§3); geometry specialist's DI registration is direct against `IAgentTool`; no `sp => sp.GetRequiredService<T>()` factory; no circular dependencies; reviewer has explicitly checked this in PR description, not assumed from passing tests
- [ ] T038 Security review — geometry specialist's Overpass query uses parameterized queries (EF Core); no raw SQL with string interpolation of user input (constitution §8); rate limiting on /api/v1/analyses endpoint; audit log written for each geometry tool execution; conforms to §8 security requirements

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+, Phase 4, Phase 5)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P1)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation
- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Models within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members
- T003-T004 (lint checks) can run in parallel with Foundational tasks

### Within Each User Story (Parallel)

- T015/T018/T022 (tests) can run in parallel
- T015/T016 (tool + DI) can run in parallel once T005-T011 (foundational) complete
- T023-T028 (US2) can run in parallel once T015 (US1 tool) is placed
- T029-T032 (US3) can run in parallel once T023 (US2) is placed

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1 — geometry finding delivers area, dimensions, frontage, orientation
4. **STOP and VALIDATE**: Run `dotnet test` for SiteAnalysis and Overpass tests — all pass
5. Deploy/demo if ready
6. Add User Story 2 → Test independently → Deploy/Demo
7. Add User Story 3 → Test independently → Deploy/Demo
8. Each story adds value without breaking previous stories

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

With multiple developers:

1. Team completes Setup + Foundational together
2. Once Foundational is done:
   - Developer A: User Story 1 (Tool + Tests)
   - Developer B: User Story 2 (Metadata + Confidence + Content)
   - Developer C: User Story 3 (Failure behaviour + Progressive delivery)
3. Stories complete and integrate independently

**Total tasks**: 38
**Task count per story**: US1: 8, US2: 7, US3: 5
**Parallel opportunities**: 12 tasks marked [P] across Phases 1-2 and within stories
**Independent test criteria**: Each user story has its own independent test suite (T018-T022 for US1, T023-T028 for US2, T029-T032 for US3)
**MVP scope**: User Story 1 only — geometry finding delivers area, dimensions, frontage, orientation as its own notice and panel