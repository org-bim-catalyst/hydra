# Tasks: Building Footprints from Rendered Map Imagery

**Input**: Design documents from `/specs/053-rendered-building-footprints/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included and **not optional** — constitution §10 requires tests for new behaviour in the
same change that introduces it, and every success criterion here is only claimable from a test.

**Organization**: Grouped by user story. US1 and US2 are both P1; US2 depends on US1 existing (the
composite needs a primary to arbitrate over), so unlike specs/052 these two are sequential.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)
- Exact file paths are given in every task

## Path Conventions

- Backend only — **this feature touches no frontend file at all**
- `src/AskLucy.Infrastructure/Buildings/` — the new providers
- `src/AskLucy.Infrastructure/Boundaries/` — the shared vectorizer being extended
- `tests/AskLucy.Infrastructure.Tests/` — all tests

**Verification commands**:

```bash
dotnet build "Ask Lucy.sln"                 # note the space in the solution name
export PERSISTENCE_TESTS_CONNECTION_STRING="<ConnectionStrings:DefaultConnection>"
dotnet test "Ask Lucy.sln" --no-build
```

---

## Phase 1: Setup

**Purpose**: Configuration and the test fixtures everything else is verified against.

- [X] T001 Create `src/AskLucy.Infrastructure/Buildings/RenderedFootprintOptions.cs` — `SectionName = "Buildings:Rendered"`, `Zoom = 18`, `MinimumAreaSquareMetres = 15.0`, `SimplifyTolerancePixels = 3.0`, `StatedPositionalToleranceMetres = 1.0`, **`CacheTtl = TimeSpan.FromMinutes(15)`** (research D3/D5/D9 — this provider caches its own results, mirroring `BuildingRetrievalOptions.CacheTtl` on the Overpass side; there is no shared cache wrapper to inherit). The **style string is a constant in the provider, not a setting** — it is verified behaviour, not an environment-varying value (§2.VII)
- [X] T002 Register the options in `src/AskLucy.Infrastructure/DependencyInjection.cs` with `.BindConfiguration(...).ValidateOnStart()`, alongside the existing `BuildingRetrievalOptions` registration
- [X] T003 Capture test fixtures into `tests/AskLucy.Infrastructure.Tests/Buildings/fixtures/` — styled PNG tiles at zoom 18 scale 2, using the exact style from contracts/rendered-footprint-provider.md, for **four** locations spanning the area types SC-001 requires (dense urban, suburban, sparse — a two-dense-urban-only fixture set cannot actually measure that criterion): Dubai — Al Safa Park (25.1558327, 55.2217644, dense urban), Cairo — prototype site (30.1327, 31.7195, dense urban / OSM-sparse), Dubai — Arabian Ranches (25.0445, 55.2708, suburban), Rub' al Khali desert edge (22.7, 54.0, sparse — verified live to return a correctly empty, all-white tile with no false positives). Record each fixture's centre, zoom and computed bounds in a sidecar JSON so tests convert pixels to coordinates without a network call

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Teach the shared vectorizer to return every component instead of only the largest.
Every user story depends on this.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Extend `src/AskLucy.Infrastructure/Boundaries/MaskContourVectorizer.cs` with `AllComponents(bool[,] mask, int width, int height, int minimumPixelArea, bool eightConnected)` — the same flood fill `LargestComponent` already performs, but yielding **every** component at or above `minimumPixelArea` instead of keeping only the largest. Refactor `LargestComponent` to share the traversal rather than duplicating it
- [X] T005 Add `ExtractAllRings(bool[,] mask, int width, int height, SatelliteImage bounds, int minimumPixelArea, double simplifyEpsilon, bool eightConnected)` to the same file — per component: `TraceOuterRing` → `DouglasPeucker` → `ToGeoRing`, returning one ring per component plus a count of components rejected for yielding fewer than 3 points. **`TryExtractRing` must remain byte-for-byte unchanged in behaviour** — it has two existing callers (the boundary extractor and the Gemini segmentation path) that this feature must not disturb
- [X] T006 Write `tests/AskLucy.Infrastructure.Tests/Boundaries/MaskContourVectorizerAllComponentsTests.cs` — synthetic masks only, no fixtures: three separated squares yield three rings; a blob below `minimumPixelArea` is excluded and counted; a degenerate 2-pixel blob is excluded and counted
- [X] T007 Write the connectivity test in the same file (quickstart Scenario 5, research D4) — two **diagonally touching** squares yield **two** rings under 4-connectivity and **one** under 8-connectivity. This is the assertion that stops a future change silently merging terraced rows into a single enormous shadow-caster
- [X] T008 Write the regression test in `tests/AskLucy.Infrastructure.Tests/Boundaries/MaskContourVectorizerAllComponentsTests.cs` proving `TryExtractRing`'s existing behaviour is unchanged — same mask, same ring before and after T004's refactor. The boundary path is shipped, working, and carries its own post-release history; this is the guard that it stays that way

**Checkpoint**: the vectorizer returns many polygons, the single-ring path is provably untouched,
and connectivity behaviour is pinned.

---

## Phase 3: User Story 1 — Shadows Appear Where Buildings Are Visible (Priority: P1) 🎯 MVP

**Goal**: Footprints retrieved from the provider's own rendering, so buildings visible on the map
cast shadows.

**Independent Test**: Run against the Cairo fixture — the site where OpenStreetMap reports zero
buildings — and confirm footprints are returned.

### Implementation for User Story 1

- [X] T009 [US1] Create `src/AskLucy.Infrastructure/Buildings/RenderedBuildingFootprintProvider.cs` implementing `IBuildingFootprintProvider` — **reuse the existing `GoogleStaticMaps` named `HttpClient`** so its 30s timeout and failure handling are inherited rather than re-derived (this is the stated budget FR-004 requires, research D1 addendum), exactly as specs/052 reused the `Overpass` client. Inject `IMemoryCache` and cache `SearchAsync`'s result under `RenderedFootprintOptions.CacheTtl`, mirroring `OverpassBuildingFootprintProvider`'s own internal caching pattern (research D9) — **this provider must cache itself; nothing wraps it**
- [X] T010 [US1] Build the request in that provider per contracts/rendered-footprint-provider.md — `maptype=roadmap`, `zoom` from options, `size=640x640&scale=2`, `format=png`, and the three style rules with `landscape.man_made.building` forced to magenta (FR-001, FR-003). **`roadmap`, not `terrain`**: specs/042 chose terrain precisely because it renders no buildings (research D1)
- [X] T011 [US1] Implement thresholding in `src/AskLucy.Infrastructure/Buildings/RenderedBuildingFootprintProvider.cs` (FR-002) — `Image.Load<Rgba32>`, a pixel is a building pixel when `R >= 180 && B >= 180 && G <= 120`, mirroring the boundary extractor's own green test in shape and tolerance so edge antialiasing is absorbed without admitting white. The decoded image and mask are local variables only — never written to disk, returned, or logged (FR-002)
- [X] T012 [US1] In `src/AskLucy.Infrastructure/Buildings/RenderedBuildingFootprintProvider.cs`, convert `MinimumAreaSquareMetres` to a pixel count using `StaticMapFraming.MetresPerPixel(latitude, zoom)` halved for `scale=2`, then call `ExtractAllRings` with **4-connectivity** (FR-006, FR-010, research D4, D5)
- [X] T013 [US1] Apply the exclusion rules from data-model.md, counting every discard into `ExcludedCount` (FR-009): below minimum area; **touching the tile edge** (clipped geometry is fabrication, research D6); degenerate after simplification; entirely outside the requested radius. A building **partly** outside the radius is deliberately **kept whole** — truncating it would cast a shadow the real building does not
- [X] T014 [US1] In `src/AskLucy.Infrastructure/Buildings/RenderedBuildingFootprintProvider.cs`, map each surviving ring to a `BuildingFootprint` — id `rendered_{tileKey}_{index}`, empty `Name`, and apply the existing site-building containment rule from specs/052 so `IsSiteBuilding` behaves identically regardless of source (FR-011, FR-019)
- [X] T015 [US1] In `src/AskLucy.Infrastructure/Buildings/RenderedBuildingFootprintProvider.cs`, honour the existing count cap, setting `Limited` exactly as the Overpass provider does (FR-005)
- [X] T016 [US1] Distinguish the two empty outcomes (contracts/rendered-footprint-provider.md): a failed or non-image fetch throws `BuildingProviderUnavailableException`; a valid tile with no building pixels returns an **empty result, not an exception**. This distinction is what drives arbitration in US2 — an exception means *this source is broken*, an empty result means *this source has nothing*
- [X] T017 [US1] Write `tests/AskLucy.Infrastructure.Tests/Buildings/RenderedBuildingFootprintProviderTests.cs` against **all four fixtures** (quickstart Scenario 1, SC-001) — Dubai and Cairo expect dozens of clean footprints, Arabian Ranches expects fewer, well-separated ones, and the desert fixture expects an **empty** result with no false positives. Every ring closed with ≥ 3 points, `ExcludedCount` populated, `Limited` honoured. Also assert a second call within the TTL does **not** re-invoke the `HttpClient` — proof the caching added in T009 actually works, not merely that it compiles
- [X] T018 [US1] Write the **Cairo** test in the same file (quickstart Scenario 2, SC-002) asserting a non-empty result at the site where Overpass returns nothing. A zero result here is a regression in the style string or threshold, not an empty neighbourhood — this single test is the feature's reason to exist
- [X] T019 [US1] Write the pixel-to-geo alignment test (quickstart Scenario 3, SC-003) — a known fixture pixel converts to its expected coordinate within the stated **±1 m**, using `StaticMapFraming.CoveredBounds`
- [X] T020 [US1] Write the failure tests in `tests/AskLucy.Infrastructure.Tests/Buildings/RenderedBuildingFootprintProviderTests.cs` — fetch failure throws the typed exception; a valid all-white tile returns empty with no exception (FR-021)

**Checkpoint**: the rendered provider works standalone and is provably better than Overpass at the
Cairo site. This is the MVP.

---

## Phase 4: User Story 2 — The Analysis Survives a Failing Source (Priority: P1)

**Goal**: Rendered is primary, Overpass is fallback, and the feature keeps working when either
fails.

**Independent Test**: Fail each source in turn with fakes and confirm footprints still arrive; fail
both and confirm the user is told while the sun path continues.

### Implementation for User Story 2

- [X] T021 [US2] Add `Source` (`rendered` | `osm` | `none`) to `src/AskLucy.Application/Buildings/BuildingFootprintResult.cs` as an **optional field with a default, appended LAST in the positional record** (FR-015) — it has two existing positional-constructor call sites (`OverpassBuildingFootprintProvider.cs`, `GetSiteBuildingsQueryHandlerTests.cs`); inserting it anywhere but last would silently break both. This is the only `Application` change in the feature — plan.md records why a log line was judged insufficient
- [X] T022 [US2] Create `src/AskLucy.Infrastructure/Buildings/CompositeBuildingFootprintProvider.cs` implementing the arbitration table in contracts/footprint-source-arbitration.md — a **decorator over two providers**, not an `if` inside one, so each source stays single-purpose and a third source later is an addition rather than an edit (§2.II)
- [X] T023 [US2] In `src/AskLucy.Infrastructure/Buildings/CompositeBuildingFootprintProvider.cs`, implement the rule exactly: primary returning ≥ 1 footprint wins; primary returning 0 **or throwing** falls through to the fallback; both empty yields an empty result with `Source = none`; both failing rethrows so the existing `503` Problem Details mapping still applies (FR-012, FR-013). **Results are never merged** (FR-014) — the two sources' polygons do not coincide, so merging would draw every building twice, slightly offset, each casting its own shadow
- [X] T024 [US2] In `src/AskLucy.Infrastructure/Buildings/CompositeBuildingFootprintProvider.cs`, log which source supplied each result via `[LoggerMessage]` structured logging, so a coverage gap degrading across a whole region is visible rather than silent (FR-015)
- [X] T025 [US2] Rewire `src/AskLucy.Infrastructure/DependencyInjection.cs` — `IBuildingFootprintProvider` now resolves to the composite; register both inner providers as their concrete types. This one line is the only change to existing backend wiring
- [X] T026 [US2] **Corrected after `/speckit-analyze`** — the original text here claimed caching "wraps the composite"; it doesn't, and never did (`OverpassBuildingFootprintProvider` has always cached internally, keyed to its own options section, with no external wrapper to inherit — research D9). Confirm instead that `CompositeBuildingFootprintProvider` wraps **neither** inner provider and holds no cache of its own — each inner provider (T009's rendered caching, the existing Overpass caching) is independently responsible for its own result
- [X] T027 [US2] Write `tests/AskLucy.Infrastructure.Tests/Buildings/CompositeBuildingFootprintProviderTests.cs` with two faked providers, asserting every row of quickstart Scenario 4's table, plus that the fallback is **never called** when the primary succeeds
- [X] T028 [US2] Write the no-merge test in `tests/AskLucy.Infrastructure.Tests/Buildings/CompositeBuildingFootprintProviderTests.cs` (FR-014) — when both sources hold footprints, the result contains those of exactly one source, never a union

**Checkpoint**: US1 and US2 together give resilient retrieval with a recorded source.

---

## Phase 5: User Story 3 — Heights Stay Honest (Priority: P2)

**Goal**: Heights and their recorded/assumed marking remain correct and visible whichever source
supplied the outline.

**Independent Test**: Retrieve rendered footprints and confirm every height is marked assumed and
none is silently presented as recorded.

### Implementation for User Story 3

- [X] T029 [US3] In `src/AskLucy.Infrastructure/Buildings/RenderedBuildingFootprintProvider.cs`, set `HeightMetres` to the existing stated default and `HeightProvenance` to **`assumed`** for every rendered footprint (FR-016, FR-017, research D8). Cross-source height matching is deliberately **not** attempted — it is a spatial join between two independently-derived geometries, and getting it wrong attaches one building's height to another's outline, which is worse than an honest default because the user is told it is recorded
- [X] T030 [US3] Write the provenance test (quickstart Scenario 7, SC-006) asserting every rendered footprint is marked `assumed` and **never** `known`. Research D8 accepts more assumed heights in exchange for footprints existing at all; this test exists so that marking can never silently drift to `known` and tell the user something false
- [X] T031 [US3] Verify specs/052's existing height-correction control still applies to rendered footprints (its FR-025) — the user's escape hatch when an assumed height is wrong matters more now that more heights are assumed (FR-018)

**Checkpoint**: all three user stories functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T032 [P] Write the exclusion-accounting test sweep (quickstart Scenario 6) — every discard reaches `ExcludedCount`; a blob partly outside the radius is kept whole; no discard is silent (FR-008, FR-009)
- [X] T033 [P] Write the failure-surface sweep (quickstart Scenario 8, SC-007) asserting each failure row resolves to a typed outcome the caller can surface, and that **zero** failures are observable only in logs (FR-021, FR-022, §2.VIII)
- [X] T034 [P] Document the feature in `src/AskLucy.Infrastructure/Buildings/README.md` — the verified style string, why `landscape.man_made` alone fails, why `roadmap` not `terrain`, the 4-connectivity choice and the shared-wall limitation, so none of it is rediscovered the hard way
- [X] T035 **SC-008 check** (quickstart Scenario 9): `git diff --name-only main -- src/AskLucy.Web/ClientApp/` must be **empty**, and `git diff --name-only main -- src/AskLucy.Application/` must show **only** `Buildings/BuildingFootprintResult.cs`. `IBuildingFootprintProvider.cs` must **not** appear — the interface fitting unchanged is the real measure that specs/052 drew the seam correctly
- [X] T036 Run the full verification sweep: `dotnet build "Ask Lucy.sln"`, then `dotnet test "Ask Lucy.sln" --no-build` with `PERSISTENCE_TESTS_CONNECTION_STRING` set
- [X] T037 Walk quickstart.md scenarios 1–10, recording each outcome. Scenarios 3 (visual overlay alignment) and 10 (end-to-end latency) have parts that cannot be measured here — mark those **not measured** with the stated human method rather than claiming a pass never observed, as specs/050, 051 and 052 each did
- [ ] T038 Live verification against real sites once merged — open solar analysis at the Dubai and Cairo sites and confirm buildings now cast shadows where the map shows them. **This is the first time the feature's actual purpose is observable**; every test above works on fixtures, and no fixture can prove the pipeline is wired correctly end to end

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies. T003 (fixtures) needs a Static Maps key
- **Foundational (Phase 2)**: depends on Setup — **blocks every user story**
- **US1 (Phase 3)**: depends on Foundational
- **US2 (Phase 4)**: depends on US1 — the composite needs a primary to arbitrate over
- **US3 (Phase 5)**: depends on US1; independent of US2
- **Polish (Phase 6)**: depends on all desired stories

### Within Each User Story

- Tests travel with the behaviour they cover; never deferred (§10)
- Provider → exclusion rules → mapping → arbitration → registration

### Parallel Opportunities

- T001/T003 in Setup
- **US2 and US3 can be built in parallel** once US1 is complete — they touch different files
- T032–T034 in Polish

---

## Parallel Example: after US1 completes

```bash
Task: "Composite arbitration provider in Infrastructure/Buildings/"        # T022 (US2)
Task: "Height provenance marking on rendered footprints"                   # T029 (US3)
```

---

## Implementation Strategy

### MVP scope — Phases 1, 2 and 3 (US1)

The rendered provider working standalone. At that point the Cairo site — where Overpass returns
nothing — produces buildings, which is the entire justification for the feature. Ship-able as a
straight swap before arbitration exists, though US2 is what makes it safe in production.

1. Phase 1 Setup
2. Phase 2 Foundational
3. Phase 3 US1 → **stop and validate** against quickstart Scenario 2
4. Demo the Cairo before/after

### Incremental delivery

US1 (rendered retrieval) → US2 (resilience) → US3 (honest heights). Each is independently testable.

---

## Notes

- **Three constraints are easy to lose and are pinned by tests**: 4-connectivity (T007), the
  tile-edge exclusion versus keeping partly-outside buildings whole (T013), and never merging
  sources (T028).
- **`TryExtractRing` must not change behaviour** (T008). It serves two shipped callers; this
  feature extends the vectorizer, it does not rewrite it.
- **The heights trade is deliberate** (T029): more assumed heights in exchange for footprints
  existing at all where OSM has none. It is recorded in research D8 and guarded by T030.
- Commit after each task or logical group; a task is not done when its test is missing.
