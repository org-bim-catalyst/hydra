# Feature Specification: Building Footprints from Rendered Map Imagery

**Feature Branch**: `053-rendered-building-footprints`

**Created**: 2026-09-13

**Status**: Draft

**Input**: User description: "Retrieve building footprints deterministically from the map provider's own rendered imagery instead of OpenStreetMap, reusing the technique specs/042 already uses to trace site boundaries."

## Context

Solar analysis (specs/052) casts shadows from the buildings around a site. Those buildings currently come from OpenStreetMap, and that is the weakest part of the feature.

The problem is coverage, not plumbing. At a test site in Cairo, OpenStreetMap reported **no buildings at all** within the search radius while the map the user was looking at showed a dense neighbourhood — so the analysis told the user there was nothing to cast shadows, over a screen full of buildings. The retrieval service is also the least reliable dependency the platform has, routinely refusing requests under load.

The map provider already knows where the buildings are, because it draws them. This feature reads that knowledge back: the platform requests a deliberately styled map image in which buildings are the only thing drawn, and converts the coloured regions into georeferenced footprints. That is not a new trick — it is exactly how the platform already traces site boundaries (specs/042), including the fact that the styled image exists only in memory and is never shown to anyone.

Heights are a separate matter. A rendered image contains no height information of any kind, so heights continue to come from the existing external tags or the stated default, exactly as they do today, regardless of where a footprint's outline came from.

## Clarifications

### Session 2026-09-13

- Q: Does the rendered source replace OpenStreetMap or supplement it? → A: It becomes the primary source, with OpenStreetMap retained as a fallback wherever the rendered source returns nothing. Replacing outright would trade one single point of failure for another, and the two sources fail for unrelated reasons — which is precisely what makes them worth keeping together.
- Q: Is the styled image ever visible to a user? → A: Never. It is requested by the platform, held in memory only for as long as the conversion takes, and discarded. Users continue to see the normal map.
- Q: Where do heights come from now? → A: Unchanged. Rendered imagery carries no height data, so height and its known/assumed provenance still come from external tags or the stated default. Footprint geometry and height are sourced independently.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Shadows Appear Where Buildings Are Visible (Priority: P1)

A user opens solar analysis on a site in a dense neighbourhood. Buildings they can see on the map cast shadows, without the user having to know or care where that geometry came from.

**Why this priority**: This is the whole point. Today a user can be looking at a screen full of buildings and be told none were found; that is the specific failure this feature exists to remove.

**Independent Test**: Open solar analysis at a location where the map visibly shows buildings but the external source has no data for them, and confirm shadows are cast by those buildings.

**Acceptance Scenarios**:

1. **Given** a site where the map shows buildings, **When** solar analysis opens, **Then** those buildings take part in the analysis and cast shadows.
2. **Given** a site where the external source has no building data but the map shows buildings, **When** solar analysis opens, **Then** the buildings still appear — the previous "no buildings found" outcome does not occur.
3. **Given** a site the map shows as genuinely empty, **When** solar analysis opens, **Then** the user is told no buildings were found, and the sun path still works.
4. **Given** buildings are retrieved, **When** the user compares them against the map, **Then** each footprint sits over the building it came from, within a stated tolerance.

---

### User Story 2 - The Analysis Survives a Failing Source (Priority: P1)

The analysis keeps working when one building source is unavailable, because the other one covers for it.

**Why this priority**: The platform's existing source fails often enough that this is a routine condition, not an edge case. A feature that only works when both sources are healthy is not an improvement.

**Independent Test**: Make each source fail in turn and confirm the analysis still produces buildings; make both fail and confirm the user is told clearly and the sun path continues.

**Acceptance Scenarios**:

1. **Given** the rendered source returns nothing for a site, **When** buildings are retrieved, **Then** the external source is consulted and its footprints are used.
2. **Given** the rendered source is unavailable, **When** buildings are retrieved, **Then** the user still gets whatever the fallback provides, without an error.
3. **Given** both sources fail, **When** buildings are retrieved, **Then** the user is told buildings could not be retrieved, and the sun path still works.
4. **Given** either source is slow, **When** the user waits, **Then** retrieval completes or gives up within a stated budget rather than hanging.

---

### User Story 3 - Heights Stay Honest (Priority: P2)

Building heights, and whether each was recorded or assumed, remain accurate and visible regardless of which source supplied the outline.

**Why this priority**: Solar analysis already promises the user it will distinguish a known height from an assumed one (specs/052 FR-011, FR-044). Changing where footprints come from must not quietly turn every height into a guess without saying so.

**Independent Test**: Retrieve footprints from the rendered source at a site where external height tags exist, and confirm heights are still applied and still marked known where recorded.

**Acceptance Scenarios**:

1. **Given** a footprint from the rendered source, **When** a recorded height exists for that building in the external source, **Then** that height is used and marked as recorded.
2. **Given** a footprint from the rendered source with no recorded height available, **When** it is shown, **Then** the stated default is used and marked as assumed.
3. **Given** any building the user inspects, **When** they view its details, **Then** they can see whether its height was recorded or assumed.

---

### Edge Cases

- What happens when two buildings touch or share a wall in the rendering? They must be handled by a stated rule rather than silently merged into one oversized footprint that casts a wrong shadow.
- What happens to a building only partly inside the analysis area? It must be either included whole or excluded by a stated rule, never truncated into a shape that casts a shadow the real building would not.
- What happens at a site where the map draws no buildings because the area genuinely has none, versus because coverage is missing? Both produce no footprints; the user is told buildings were not found either way, and the distinction is not claimed.
- What happens when a rendered region is too small to be a real building? It must be discarded by a stated minimum size rather than becoming a speck that casts a shadow.
- What happens when the two sources disagree — both return buildings for the same site? A stated rule decides which is used; results are never merged into overlapping duplicates.
- What happens over water, desert, or at very low zoom where the rendering carries no building detail? No footprints, stated plainly.
- What happens when retrieval succeeds but produces an implausible number of footprints? The count must be bounded and the user told when data was limited, as it already is today.

## Requirements *(mandatory)*

### Retrieval

- **FR-001**: The system MUST retrieve building footprints for a location and bounded radius from the map provider's own rendered imagery.
- **FR-002**: The styled imagery used for retrieval MUST be requested by the platform, held only for the duration of the conversion, and MUST NOT be shown to any user.
- **FR-003**: The styling used MUST isolate buildings from all other map content, so that no road, land parcel, park, water body or label is mistaken for a building.
- **FR-004**: Retrieval MUST complete or fail within a stated time budget.
- **FR-005**: The number of footprints returned MUST be bounded, and the user MUST be told when the result was limited.

### Conversion

- **FR-006**: Each coloured region in the retrieved imagery MUST be converted into a closed polygon expressed in real-world coordinates.
- **FR-007**: Converted footprints MUST align with the buildings they represent to a stated positional tolerance.
- **FR-008**: Regions smaller than a stated minimum area MUST be discarded rather than returned as footprints.
- **FR-009**: Regions that cannot be converted into a usable closed polygon MUST be excluded without failing the whole retrieval, and the number excluded MUST be reported.
- **FR-010**: Buildings that touch or share a boundary in the rendering MUST be separated by a stated rule, or the limitation MUST be stated.

### Sources and fallback

- **FR-011**: The rendered source MUST be the primary source of footprints.
- **FR-012**: When the rendered source returns no footprints or is unavailable, the system MUST fall back to the existing external source.
- **FR-013**: When both sources fail or return nothing, the user MUST be told, and solar analysis MUST continue to work without buildings.
- **FR-014**: When both sources return footprints for the same site, a stated rule MUST decide which is used; results MUST NOT be merged into overlapping duplicates.
- **FR-015**: Which source supplied a given result MUST be recorded for diagnosis.

### Heights

- **FR-016**: Building height MUST continue to be sourced independently of footprint geometry.
- **FR-017**: A footprint from the rendered source MUST still receive a recorded height where one is available, and MUST use the stated default otherwise.
- **FR-018**: Whether a height was recorded or assumed MUST remain visible to the user, unchanged from today.

### Compatibility

- **FR-019**: This feature MUST NOT change how solar analysis consumes building data — the shape of what it receives stays the same.
- **FR-020**: This feature MUST NOT change solar analysis's existing user-visible behaviour beyond more buildings being found more often.

### Failure

- **FR-021**: Every failure — either source unavailable, nothing found, conversion unusable, result limited — MUST produce a user-understandable explanation and MUST be recorded for diagnosis.
- **FR-022**: No failure introduced by this feature may be observable only in logs.

### Key Entities

- **Building Footprint**: A closed outline of one building in real-world coordinates, with a height, a record of whether that height was recorded or assumed, and which source supplied the outline.
- **Retrieval Area**: The location and bounded radius a retrieval covers.
- **Retrieval Result**: The footprints found, whether the result was limited, how many regions were excluded as unusable, and which source was used.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At a set of test locations spanning dense urban, suburban and sparse areas, buildings visible on the map are returned as footprints in at least 90% of cases.
- **SC-002**: At locations where the external source returns nothing but the map shows buildings, footprints are returned — the previous "no buildings found" outcome is eliminated for those locations.
- **SC-003**: Returned footprints align with the buildings they represent within a stated tolerance, verified by visual comparison at a set of test locations.
- **SC-004**: Solar analysis produces buildings when either source is available, verified by failing each in turn.
- **SC-005**: Retrieval completes within its stated time budget at 95% of test locations.
- **SC-006**: Heights and their recorded/assumed marking are unchanged from today for the same buildings, verified at locations where both sources have data.
- **SC-007**: Every failure produces a user-visible explanation — zero failures are observable only in logs.
- **SC-008**: Solar analysis requires no change to how it consumes building data.

## Scope

### In Scope

- Retrieving building footprints from the map provider's rendered imagery for a location and bounded radius.
- Converting rendered regions into georeferenced closed polygons.
- Falling back to the existing external source, and deciding between sources when both return data.
- Preserving today's height sourcing and its recorded/assumed marking.
- Bounding, exclusion and failure reporting consistent with what solar analysis already does.

### Out of Scope

- Building heights from imagery. Rendered maps carry no height data; this feature changes footprint geometry only.
- Roof shape, floor counts, building use, or any attribute beyond the footprint outline.
- Replacing the external source outright — it is retained deliberately as a fallback.
- Any change to solar analysis itself beyond receiving better building data.
- Using rendered imagery for anything other than buildings.
- Caching strategy changes beyond what the existing building retrieval already does.

## Assumptions

- The map provider's rendered imagery can be styled so buildings are isolated from all other content. This has been confirmed by live verification at two test locations before this specification was written, in both a dense Dubai neighbourhood and a Cairo neighbourhood where the external source had no data at all.
- Building coverage in the provider's rendering is at least as good as the external source in the areas this platform serves, and materially better in some. The Cairo case — dozens of buildings rendered where the external source reported none — is the evidence.
- Rendered imagery gives outlines only. Any attribute beyond geometry must come from elsewhere, which is why height sourcing is explicitly unchanged.
- The existing site-boundary tracing already establishes that this technique works, is acceptable under the provider's terms as used here, and that an in-memory styled image is an acceptable pattern in this platform.
- Positional accuracy is bounded by the resolution of the rendered image at the zoom used, so the stated tolerance is inherited from that rather than being arbitrary.
- Retrieval remains a platform-side operation, keeping third-party imagery and provider credentials off the client, consistent with how boundaries and the current building lookup already work.

## Dependencies

- **specs/052-solar-analysis** — the consumer. This feature changes where its building data comes from and nothing else.
- **specs/042-site-boundary-resolution** — establishes the styled-imagery tracing technique, the request framing and the in-memory handling this feature reuses.

## Downstream

Once footprints come from the provider's rendering, the same technique could supply other map
features the platform currently lacks data for. Nothing here assumes that, and no seam is built
for it.

### Alternatives evaluated and rejected (2026-09-13)

Three ready-made sources were evaluated before settling on rendered imagery. All three are better
products than what this feature builds, and all three were rejected for the same reason: **none
covers this platform's primary market.** That is recorded here in detail so the evaluation is not
repeated from scratch, and so the conditions under which each becomes the right answer are written
down rather than remembered.

| Source | Would have given | Verdict |
|---|---|---|
| Published open building footprints | Vector footprints, confidence, area | Gulf not covered; no heights |
| Published 2.5D building heights | Measured heights, ~4 m resolution | Gulf not covered; accuracy unvalidated where it *is* covered |
| Provider's own solar analysis service | Roof pitch, azimuth, **hourly shade** | Gulf not covered |

The coverage finding was verified directly against each source's own published coverage data, not
inferred: the primary Dubai site and the Cairo test site both fall outside every quality tier of
all three, while control points in North America, Europe, Japan and Australia fall inside — so the
test itself is sound.

**The pattern matters more than any single result.** All three are derived from aerial or
satellite processing programmes that have not reached the Arabian Peninsula. Any future source of
that kind should be assumed to have the same gap until checked. This is precisely what makes the
rendered-imagery approach the right one here: it reads the basemap the provider already draws
everywhere, rather than a derived dataset with a coverage frontier.

**The third one is the most valuable to revisit.** The provider's own solar service returns roof
pitch, azimuth and pre-computed hourly shade — it would not merely feed specs/052's shadow
computation, it would replace a substantial part of it. If coverage ever extends to this
platform's regions, that is a deliberate re-evaluation of specs/052's whole approach, not a
drop-in swap, and it should be taken as such rather than stumbled into.

### Measured building heights (evaluated 2026-09-13, deliberately not taken)

Heights remain this area's weakest data: most buildings fall back to a stated default because the
external source rarely records one. A published dataset of *measured* building heights was
evaluated as a way to fix that, and rejected for now. The reasoning is recorded here so it is not
re-litigated from scratch, and so the conditions under which it would become the right answer are
written down.

What it offers: annual height rasters, roughly 4 m effective resolution, derived from satellite
imagery, openly licensed.

Why it was not taken:

- **It does not cover this platform's primary market.** Its coverage is Africa, South and
  South-East Asia, Latin America and the Caribbean. The Gulf is outside it. A height source that
  works in Cairo but not Dubai cannot be the primary one here.
- **Its stated accuracy was not measured where it applies.** The published error figure was
  evaluated in North America, Europe and Japan — none of which the dataset covers. The number that
  makes it attractive is therefore unvalidated in the regions where it would actually be used, and
  this platform's convention is to inherit stated accuracy rather than borrow an unrelated one.
- **It is raster, not vector.** Using it means sampling a height surface beneath each footprint and
  reducing it to a single figure, and at 4 m resolution a small building's sample is contaminated
  by whatever stands next to it.

Why it would still be a clean fit later: specs/052 already sources height independently of
footprint geometry (its FR-016, restated here as FR-016 to FR-018), so a new height source is an
additive implementation behind an existing seam, not a rewrite. If coverage extends to this
platform's regions, or if accuracy is published for regions it does cover, revisiting this is a
contained change — and the known/assumed distinction the user already sees is exactly the place a
third provenance value ("measured") would slot in.
