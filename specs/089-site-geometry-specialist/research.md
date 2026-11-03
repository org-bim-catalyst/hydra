# Phase 0 Research: Site Geometry Specialist

All unknowns were resolvable from the current tree — SPEC-057's design artifacts, SPEC-042's boundary
machinery, SPEC-088's provider/tool conventions and SPEC-090's capture rules. No
`NEEDS CLARIFICATION` markers remain.

## D1 — The measurement basis is the frozen boundary snapshot

**Decision**: Measure the site from `SiteAnalysis.BoundaryGeoJson`, the polygon written when the
analysis was dispatched, together with `SiteAnalysis.Latitude`/`Longitude`. Never re-resolve the site
by name, and never geocode again.

**Rationale**: A geometry finding must describe **the site being analysed**. Re-resolving a name can
resolve a different place (the same defect class SPEC-090 closed for the map capture), and it would
also make the geometry finding depend on a second, independent resolution — exactly the "second
site-identification path" the spec forbids. The snapshot is deliberately frozen on the aggregate
("a later boundary re-resolution must not retroactively change what an analysis was run against"), so
it is the correct basis for every figure.

**Current-tree evidence**: `SiteAnalysis.BoundaryGeoJson` stores
`JsonSerializer.Serialize(boundary.Polygon)` where the polygon is `IReadOnlyList<GeoPoint>` — a JSON
array of `{ "latitude": …, "longitude": … }` (PascalCase, default `JsonSerializer` options). No
reader parses it today, so the parser is new (`SiteBoundarySnapshot`).

**Alternatives considered**:
- *Re-run `IBoundaryResolutionService` inside the specialist* — rejected: a second resolution can
  disagree with the analysis's own site, costs another Overpass + imagery round trip, and violates
  FR-005.
- *Read `UserChat.ActiveBoundary` instead* — rejected: it is the chat's current outline, which may
  have changed since dispatch (SPEC-079 manual corrections). The analysis must not drift.
- *Enrich the snapshot with source/confidence* — deferred (see Complexity Tracking): it changes the
  shared dispatch write path and legacy rows still need the fallback, for no accuracy gain.

---

## D2 — Parcel and street data come from one bounded Overpass query

**Decision**: A single Overpass request per analysis asks for:
- **parcel outlines**: `way`/`relation` with `boundary=parcel` or `boundary=cadastral`;
- **streets**: `way` with `highway` in a curated road-class list (motorway … living_street,
  pedestrian, and their `_link` variants; **not** footway/cycleway/path).

The search radius is derived from the site's own extent — `max(60, ceil(diagonal/2) + 80)` metres,
capped at **1,500 m**; a point-only site uses 120 m.

**Rationale**: One request keeps the specialist inside a bounded cost the spec requires (FR-031) and
matches the platform's existing Overpass discipline (a `[timeout:25]` server budget, one client call
per attempt). The radius is derived from the site rather than a fixed constant so a large site is not
clipped, while the cap keeps a mis-resolved district-scale polygon from turning into a metropolis-sized
download. The curated highway list is what makes "frontage" mean *street* frontage: a footpath is not a
street, and counting one would inflate the figure.

**Alternatives considered**:
- *Two requests (parcels, then streets)* — rejected: doubles the cost of the flakiest upstream for no
  separation benefit; the results are split by tags locally.
- *`highway=*` any value* — rejected: returns footways, steps, service alleys and raceways, which are
  not frontage.
- *`building` footprints as a proxy for site shape* — rejected: the spec measures the site's own
  geometry, not its buildings.

---

## D3 — Introduce the shared `IOverpassFeatureQueryProvider`; fold the boundary provider onto it

**Decision**: Add `Application/Overpass/IOverpassFeatureQueryProvider` with
`OverpassFeatureQuery`/`OverpassTagFilter`/`OverpassFeature` records, implemented in
`Infrastructure/Overpass/OverpassFeatureQueryProvider` over the existing named `"Overpass"` client and
`OverpassOptions` (same endpoints, mirrors, 3 attempts, 1 s delay, UA header, 30 s timeout,
`[out:json][timeout:25]`, `out geom`). Fold `OverpassBoundaryCandidateProvider`'s transport and query
building onto it, keeping its candidate interpretation and adjacent-part merge untouched.

**Rationale**: The brief requires the abstraction if it does not exist (it does not — verified by
search), and the constitution's DRY threshold ("until a third real use case proves the abstraction is
warranted") is now met: boundary search, geometry parcels, geometry streets. Copying the transport a
fourth time would create four places where retry/mirror/timeout bugs can diverge — and this codebase
has already paid for those bugs twice (the saturated load balancer, the 15 s false-unavailability).

**Query semantics**: `OverpassTagFilter(Key, Values?, IncludeRelations)` reproduces the boundary
provider's existing rules exactly — a `null` value means "any value, ways only"; explicit values also
query `relation["type"="multipolygon"]` when `IncludeRelations` is set. Ways return their geometry
as-is (open or closed); relations return their largest assembled outer ring, using the ring-assembly
code extracted from the boundary provider into `OverpassGeometry`.

**Alternatives considered**:
- *Implement the abstraction and leave the boundary provider alone* — rejected: that ships a fifth
  copy instead of removing three, which is the opposite of what the abstraction is for.
- *Migrate `OverpassRelatedSiteBuildingProvider` and `OverpassBuildingFootprintProvider` too* —
  deliberately deferred: their shapes differ (named nodes joined onto footprints; paged, cached
  footprint retrieval), and migrating them would change proven behaviour without a feature need. The
  abstraction is built to accept them; the migration is a mechanical follow-up.

---

## D4 — Parcel identification: containment, then smallest area

**Decision**: Among the returned parcel polygons, choose those that **contain the analysed point**
(`GeometryMath.Contains`). If several do, take the smallest area (the most specific outline). If none
contains the point, report "no parcel boundary recorded for this site". The chosen parcel is reported
as a **parcel boundary**, separately from the site boundary used for measurement.

**Rationale**: A parcel that does not contain the site's own point is a neighbour, and reporting it as
"the site's parcel" would be worse than reporting none. Smallest-area containment is the standard way to
resolve nested outlines (a parcel inside a land-use polygon). The finding never substitutes one shape
for the other, which is the spec's central honesty requirement (FR-008/FR-009).

**Alternatives considered**:
- *Nearest parcel by distance* — rejected: neighbours are often nearer than a large containing parcel,
  and "near" is not "this site".
- *Require the parcel to match the site boundary's area within a ratio* — rejected: a real parcel may
  legitimately be larger than the resolved site outline; the figure is reported as measured, with its
  own area visible to the reader.

---

## D5 — Frontage: boundary edges attributed to the nearest named street

**Decision**: For each boundary edge, sample points along it; find the nearest street way within
**25 m**. If the nearest street has a name (`name`, else `name:en`), attribute that edge's full length
to it. Sum per street and report a total. If no edge is within tolerance of a **named** street, report
frontage as unavailable with the reason ("no named street within 25 m of the site boundary").

**Rationale**: Frontage is a street-adjacency measurement, not a perimeter. The 25 m tolerance covers
OSM road centrelines versus a parcel edge across a verge or service strip; it is a documented constant
so a reader can understand the method, and tightening it would silently drop real frontages on wide
roads. Requiring a **name** is what makes FR-010's "per identifiable street" true: unnamed service
ways cannot be named in the finding, and lumping them under one label would misrepresent several
streets as one. Edges that front nothing contribute zero, so the figure is genuinely street-facing
length.

**Alternatives considered**:
- *Longest edge as a stand-in* — rejected explicitly by the spec (FR-011): it invents a frontage.
- *Count unnamed roads under "Unnamed road"* — rejected: it would present a set of distinct streets as
  one, and the figure could not be checked by the reader.
- *Geodesic buffer intersection* — rejected: needs a geometry library and is disproportionate at site
  scale.

---

## D6 — Dimensions and orientation from a principal-axis projection

**Decision**: Project the ring into local metres (`GeometryMath.ToLocalMeters`, x = east, y = north),
compute the vertex covariance's principal axis, and report:
- **dimensions** = extent along the principal axis × extent perpendicular to it (larger first);
- **orientation** = the compass bearing of the principal axis, rendered as a named direction with its
  bearing (e.g. "north-east (48°)").

If the two extents are within **5%** of each other, orientation is reported as not meaningful (a
near-square has no defensible principal direction) while dimensions are still reported.

**Rationale**: A bounding box in lat/lon axes overstates a rotated site; projecting onto the site's own
axis reports the extents a user reads off a plan. The 5% rule prevents inventing a direction for a
square — an arbitrary choice between two nearly equal axes would look like a measurement. The compass
name satisfies FR-014 (a direction a user can act on) while the bearing keeps it checkable.

**Alternatives considered**:
- *Minimum-area bounding rectangle (rotating calipers)* — marginally tighter, more code and more edge
  cases; the vertex-covariance axis is adequate at site scale and is explainable.
- *Latitude/longitude bounding box* — rejected: wrong for any site not axis-aligned, which is most.

---

## D7 — Confidence: rule-based, never `Importance`

**Decision**:
- parcel outline found and measured → **High** (`dataDirectlyDescribesSite: true`);
- figures measured from the analysis's resolved site boundary → **Medium**
  (`dataIsSparseOrApproximate: true`);
- no boundary at all → **Low** (`false, false`).

`SiteAnalysisConfidence.ResolveConfidence` is the only assignment path.

**Rationale**: SPEC-057 D10 — Google's and Nominatim's `Importance` are on incompatible scales and
already caused a production defect. The three levels map cleanly onto how directly the source describes
*this* site: a recorded parcel is about the site itself; the resolved boundary is the platform's best
shape for it; nothing is measured when there is no shape.

**Alternatives considered**:
- *Use the boundary's own confidence score* — impossible: the snapshot does not persist it (D1), and
  reaching for it later would reintroduce a provenance field whose scale is unrelated to this rule.
- *Lower boundary-derived figures to Low* — rejected: the resolved boundary is a real, platform-produced
  shape of this site, not model inference.

---

## D8 — Composition: `ComposeSections`, no image

**Decision**: One finding composed with `SiteAnalysisContentComposer.ComposeSections`:
`Measurements` (area, dimensions, orientation), `Frontage` (per street + total, or the unavailable
reason), `Boundary` (parcel outline found/not recorded; the site boundary used for measurement).
Provenance block last. `documentId: null`. No image, no map capture.

**Rationale**: SPEC-088 added `ComposeSections` precisely for multi-group findings, and the client
already renders its blocks. An image would duplicate the schematic specialist's output and would make
the geometry finding's scope ambiguous (spec Clarifications). `MapCapture` (SPEC-083) is available but
deliberately unused: this finding's subject is measurement.

**Alternatives considered**:
- *`ComposeFinding`* — cannot express labelled groups.
- *A map capture highlighting the boundary* — rejected: imagery is the other specialist's output; the
  spec records the decision so it is not read as an oversight.

---

## D9 — Failure model: an outage downgrades figures; a fault fails the finding

**Decision**: `BoundaryProviderUnavailableException` (and any other provider failure) is **not**
fatal: the tool reports a successful finding whose source-dependent figures (parcel, frontage) carry
the unavailable reason, while boundary-derived figures are still delivered, and confidence is capped at
Medium. A genuine fault — the analysis record cannot be read, or an unexpected exception — calls
`ReportFailureAsync` with the cause and returns `AgentToolResult.Failure`. Caller cancellation
rethrows. An unparseable `BoundaryGeoJson` is logged and stated in the finding as an unavailable
boundary, not silently treated as "no boundary".

**Rationale**: The figures that need no network are still true when Overpass is down, and the spec's
honesty model (every figure states its own grounding) is exactly what unavailable figures are for.
Killing the whole finding would withhold measurements the platform already holds. A true fault is a
defect and must be captured (constitution §2 VIII), not dressed up as missing data.

**Alternatives considered**:
- *Fail the specialist on any source error* — rejected: withholds correct area/dimension figures and
  contradicts the environmental specialist's established "unavailable figure" pattern.
- *Treat an unreadable snapshot as an empty one* — rejected: silent data-integrity failure.

---

## D10 — Tests mirror the shipped specialists' patterns

**Decision**:
- `SiteGeometryToolTests` mirrors `EnvironmentalContextToolTests` (substituted
  `ISiteAnalysisRepository` + `ISiteGeometryDataProvider` + relay, content-kind assertion) and covers:
  parcel found → High, no parcel → Medium with the boundary stated, no boundary → figures unavailable,
  provider unavailable → unavailable figures + success, missing analysis → `ReportFailureAsync` +
  failure, missing input id → failure without a relay call.
- `SiteGeometryCalculatorTests` covers area/dimensions/orientation (including a rotated rectangle and
  the near-square rule), frontage attribution and tolerance, and the regular-buffer detection.
- `SiteBoundarySnapshotTests` covers PascalCase/camelCase arrays, null, invalid JSON and short rings.
- `OverpassFeatureQueryProviderTests` mirrors `OverpassBoundaryCandidateProviderTests`
  (`StubHttpMessageHandler`): query text for filters with/without values and relations, mirror
  fallover after 504, `BoundaryProviderUnavailableException` mapping, open-way and relation parsing.
- `OverpassSiteGeometryDataProviderTests` covers parcel containment/smallest-area selection, split of
  streets from parcels, and the no-candidate outcome.
- `OverpassBoundaryCandidateProviderTests` stays **unchanged** as the regression guard for the
  transport refactor.

**Rationale**: Constitution §10 requires tests in the same change; the repo's existing fixtures and
stub handlers are reused rather than replaced, so the new tests read like the ones beside them.

**Alternatives considered**: live Overpass calls in CI — rejected (flaky, slow; SPEC-042 research #12).
