# Quickstart: Rendered Building Footprints Validation

One scenario per success criterion. Scenarios that genuinely cannot be performed in this
environment say so and state the method for a human, rather than claiming a pass never observed —
the same honesty specs/050, 051 and 052 each applied.

## Prerequisites

```bash
# The Static Maps key already lives in user-secrets (Geocoding:GoogleMapsApiKey).
# Backend tests additionally need the dev connection string:
export PERSISTENCE_TESTS_CONNECTION_STRING="<ConnectionStrings:DefaultConnection>"

dotnet build "Ask Lucy.sln"          # note the space in the solution name
dotnet test "Ask Lucy.sln" --no-build
```

Most scenarios below run offline against **checked-in raster fixtures**, so vectorisation is
verified deterministically without a network call or an API key.

---

## Scenario 1 — Buildings appear where the map shows them (SC-001, US1)

**Run**: `dotnet test --filter "FullyQualifiedName~RenderedBuildingFootprintProviderTests"`

**Corrected after `/speckit-analyze`**: SC-001 requires locations "spanning dense urban, suburban
and sparse areas" — the original two fixtures (Dubai, Cairo) were both dense urban, so the claim
could not actually be measured. Four fixtures now cover the spread:

| Fixture | Area type | Verified outcome |
|---|---|---|
| Dubai — Al Safa Park | Dense urban | Dozens of clean footprints |
| Cairo — prototype site | Dense urban (OSM-sparse) | Dozens of clean footprints where OSM has none |
| Dubai — Arabian Ranches | Suburban (low-density villas) | Isolated footprints, correctly separated |
| Rub' al Khali desert edge | Sparse | Correctly empty — no false positives |

Vectorises each and asserts the footprint count is within the expected range for its type, every
ring is closed, and each has ≥ 3 points. The desert fixture asserts an **empty** result specifically
— confirming the pipeline reports nothing rather than manufacturing noise where there is nothing to
find.

**Manual, needs a key**: `GET /api/v1/site-buildings?latitude=25.1558327&longitude=55.2217644` with
a bearer token → footprints returned, `source: "rendered"`.

---

## Scenario 2 — Coverage where the incumbent has none (SC-002, US1)

**This is the feature's reason to exist**, so it is checked explicitly rather than folded into
Scenario 1.

**Run**: `dotnet test --filter "FullyQualifiedName~RenderedBuildingFootprintProviderTests.Cairo"`

The Cairo fixture is the site where Overpass reports **zero** buildings within 200 m. The test
asserts the rendered source returns a non-empty result there.

**Expect**: dozens of footprints. A zero result is a regression in the style string or the
threshold, not an empty neighbourhood.

---

## Scenario 3 — Footprints land on the right buildings (SC-003)

**Run**: `dotnet test --filter "FullyQualifiedName~PixelToGeoAlignment"`

Asserts that a known pixel in a fixture converts to the expected coordinate within the stated ±1 m
(research D3), using `StaticMapFraming.CoveredBounds` — the same conversion the boundary path uses.

**Not measured in this environment**: visual confirmation that a footprint overlays the building it
came from. **Method for a human**: open solar analysis with the developer massing toggle on
(specs/052 FR-010), and compare the extruded footprints against the basemap's own 3D buildings.
They should coincide within roughly a metre. This is the check that would catch a systematic
half-tile offset, which no unit test can.

---

## Scenario 4 — Both sources keep the feature alive (SC-004, US2)

**Run**: `dotnet test --filter "FullyQualifiedName~CompositeBuildingFootprintProviderTests"`

With two faked providers, assert each row of the arbitration table:

| Primary | Fallback | Expect |
|---|---|---|
| returns footprints | — | used, `source = rendered`, fallback never called |
| returns empty | returns footprints | fallback used, `source = osm` |
| throws | returns footprints | fallback used, `source = osm`, no error surfaced |
| returns empty | returns empty | empty, `source = none`, **not** an exception |
| throws | throws | rethrows → `503` Problem Details |

**Also asserts**: results are **never merged** — no case returns footprints from both (FR-014).

---

## Scenario 5 — Touching buildings stay separate (FR-010, research D4)

**Run**: `dotnet test --filter "FullyQualifiedName~Connectivity"`

A synthetic mask with two diagonally-touching squares must yield **two** footprints, not one.
This is what 4-connectivity buys, and an 8-connected regression would silently merge terraced rows
into one enormous shadow-casting block.

**Also asserts the stated limitation**: two squares sharing a full edge yield **one** footprint.
Asserting the limitation keeps it honest rather than aspirational.

---

## Scenario 6 — Exclusions are counted, not silent (FR-008, FR-009)

**Run**: `dotnet test --filter "FullyQualifiedName~Exclusion"`

| Input | Expect |
|---|---|
| Blob below 15 m² | excluded, `ExcludedCount` incremented |
| Blob touching the tile edge | excluded (research D6 — clipped geometry is fabrication) |
| Blob partly outside the radius | **kept whole** — the one deliberate non-exclusion |
| Degenerate ring (< 3 points) | excluded and counted |

**Expect**: no discard is silent. Every one reaches `ExcludedCount`, which specs/052 already
surfaces to the user.

---

## Scenario 7 — Heights are unchanged and honest (SC-006, US3)

**Run**: `dotnet test --filter "FullyQualifiedName~HeightProvenance"`

**Expect**: every rendered footprint carries the stated default height marked **`assumed`** — never
`known`. Research D8 accepts this trade (more assumed heights in exchange for footprints existing
at all); the test exists so the marking can never silently drift to `known`, which would tell the
user something false.

---

## Scenario 8 — Every failure is visible (SC-007, FR-021, FR-022)

**Run**: the provider and composite test suites.

| Forced failure | Expected surface |
|---|---|
| Tile fetch fails | Fall back; if both fail, `503` Problem Details |
| Tile has no building pixels | Empty result, `source = none` → "no buildings found" |
| Both sources empty | Same, sun path still works (specs/052 FR-014) |
| Every component excluded | Empty result with `ExcludedCount > 0` — distinguishable from "nothing there" |

**Expect**: zero failures observable only in logs.

---

## Scenario 9 — specs/052 is untouched (SC-008, FR-019)

```bash
git diff --name-only main -- src/AskLucy.Web/ClientApp/
git diff --name-only main -- src/AskLucy.Application/
```

**Expect**:

- **Frontend: empty.** No `ClientApp` file changes at all. specs/052 consumes building data through
  an unchanged endpoint returning an unchanged shape.
- **`Application`: exactly one file** — `Buildings/BuildingFootprintResult.cs`, gaining the single
  additive `Source` field FR-015 requires. Nothing else.

Anything beyond that means the seam specs/052 defined was not sufficient after all — a finding
worth recording against that spec, exactly as specs/052 did for its own predecessors.

**Also assert**: `IBuildingFootprintProvider.cs` is **not** in the diff. The interface fitting
unchanged is the real measure of whether specs/052 drew the seam in the right place; a change there
would be the genuine finding, and a new optional field on a result record would not.

---

## Scenario 10 — Retrieval stays within budget (SC-005, FR-004)

**The stated budget is 30 seconds**, inherited from the reused `GoogleStaticMaps` client's own
`Timeout` (research D1 addendum) — not a new number invented for this feature.

**Run**: `dotnet test --filter "FullyQualifiedName~Buildings"` and note durations.

**Partially measurable here**: vectorisation time over a 1280² fixture is measurable offline and
should be well under a second — nowhere close to the 30 s budget on its own. **Not measured**: real
end-to-end latency including the tile fetch, i.e. whether the 30 s budget is actually met in
practice rather than merely configured. **Method for a human**: call the endpoint against several
live sites and record wall-clock time; confirm it stays under 30 s at 95% of them (SC-005) and
compare against the existing Overpass path, which this should comfortably beat — one tile fetch
versus a query against a frequently-saturated public service.

---

## Actual outcomes (T037)

Recorded after implementation, against the 44 backend tests this feature added
(`AskLucy.Infrastructure.Tests`, 317 → 361) and the full-solution sweep (T036: all 2,295 backend
tests pass, 0 failures — Domain 216, Application 1,318, Infrastructure 361, Web 350, Persistence
46 + 8 intentionally-skipped scale tests).

| Scenario | Outcome |
|---|---|
| 1 — Buildings appear where the map shows them | **Pass.** All four live fixtures (`dubai-al-safa-park`, `cairo-prototype-site`, `dubai-arabian-ranches`, `rub-al-khali-desert-edge`) produce the expected footprint shape: dense-urban fixtures yield dozens of closed rings, the suburban fixture yields fewer well-separated ones, the desert fixture yields none. |
| 2 — Coverage where the incumbent has none | **Pass.** The Cairo fixture — verified before this spec was written to have zero OSM buildings within 200 m — returns dozens of footprints from the rendered path. |
| 3 — Footprints land on the right buildings | **Partial.** `CoveredBounds` reproduces the independently-computed (JavaScript, at capture time) bounds for all four fixtures to 1e-6 precision — the geometry math is confirmed correct. **Not measured**: visual overlay-vs-basemap alignment, exactly as quickstart states — needs a live browser and the developer massing toggle. |
| 4 — Both sources keep the feature alive | **Pass.** Every row of the arbitration table exercised with faked providers: primary wins outright, empty/throwing primary falls back, both-empty yields `Source = none` (not `Osm`), both-throwing rethrows. |
| 5 — Touching buildings stay separate | **Pass.** Diagonally-touching squares: 2 components at 4-connectivity, 1 at 8-connectivity. The stated shared-wall limitation is itself asserted (two squares sharing a full edge merge even at 4-connectivity). |
| 6 — Exclusions are counted, not silent | **Pass — after a real fix.** A synthetic tile with all four exclusion cases (below-minimum-area, tile-edge, entirely-outside-radius, straddling-radius/kept-whole) initially found `ExcludedCount` undercounting by exactly the below-minimum-area case: `AllComponents` filters those out before `ExtractAllRings`'s loop ever sees them, so they were never being counted. Fixed in `MaskContourVectorizer.ExtractAllRings` by comparing against an unfiltered component count. See `src/AskLucy.Infrastructure/Buildings/README.md`. |
| 7 — Heights are unchanged and honest | **Pass.** Every rendered footprint across all four fixtures is `Assumed`, never `Known`; height equals Overpass's own 9.0 m default exactly. |
| 8 — Every failure is visible | **Pass.** Fetch failure, timeout, missing API key and both-sources-failing all rethrow the typed exception the existing `503` Problem Details mapping already handles (untouched by this feature); a valid empty tile returns a normal empty result, never an exception. |
| 9 — specs/052 is untouched | **Pass.** `git diff --name-only main -- src/AskLucy.Web/ClientApp/` is empty. `git diff --name-only main -- src/AskLucy.Application/` shows exactly one file, `Buildings/BuildingFootprintResult.cs` (one additive field). `IBuildingFootprintProvider.cs` does not appear — the interface fits unchanged. |
| 10 — Retrieval stays within budget | **Partial.** Vectorisation over real 1280² fixtures completes in milliseconds per the full suite's ~5 s total for 44 tests. **Not measured**: real end-to-end tile-fetch latency, as stated above. |

### One design correction found during implementation, beyond the caching fix already in research D9

`CompositeBuildingFootprintProvider`'s constructor originally took the two concrete provider types
directly (per the contract as planned). Both `RenderedBuildingFootprintProvider` and
`OverpassBuildingFootprintProvider` are `sealed`, and NSubstitute cannot proxy a sealed class — a
concrete-type constructor could never actually be unit-tested, which would have meant Scenario 4
above was untestable as designed. Fixed by depending on `IBuildingFootprintProvider` disambiguated
via keyed DI (`"rendered"`/`"osm"`) instead — fully fakeable, and a better dependency direction
besides (constitution §2.V). Recorded in `contracts/footprint-source-arbitration.md`.
