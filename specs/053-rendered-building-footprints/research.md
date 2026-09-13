# Phase 0 Research: Building Footprints from Rendered Map Imagery

The feature's premise was verified live **before** the specification was written, so this phase
spends its budget on the conversion step — where the real unknowns are — rather than re-proving
that the styling works.

---

## D1 — The style string (verified live, not designed)

**Decision**: request a Static Maps tile with `maptype=roadmap` and exactly three style rules:

```
feature:all|element:labels|visibility:off
feature:all|element:geometry|color:0xffffff
feature:landscape.man_made.building|element:geometry|color:0xff00ff
```

**Rationale**: this produces a **binary** image — solid magenta building footprints on pure white,
with no roads, land parcels, parks, water or labels present. Thresholding it is trivial, which is
what makes the rest of the pipeline simple.

**Verified at two locations** before specification: a dense Dubai neighbourhood (Al Safa Park) and
the Cairo site where OpenStreetMap reports zero buildings within 200 m. Both produced clean,
separable footprints. The Cairo result — dozens of buildings where the incumbent source has none —
is the feature's justification.

**Two alternatives were tried and are recorded because both look plausible and both fail:**

- **`landscape.man_made` (no `.building`)** — floods the *entire urban fabric*. Every city block
  renders magenta; buildings are distinguishable only as faint white outlines inside it. Completely
  unusable for thresholding. This is the obvious first attempt and it is wrong.
- **`landscape.man_made` stroke only** (`element:geometry.stroke`) — does isolate buildings, as
  1-pixel outlines on white. It works, but it needs contour *closing* before filling, and a single
  broken pixel merges a building with its neighbour. Strictly worse than the filled result below.

**`landscape.man_made.building` is a valid feature type** in the legacy Static Maps style schema
even though it is not prominent in the documentation. This matters: the Cloud-styling schema the
platform's vector Map ID uses (`infrastructure.building`, specs/048) is a *different vocabulary*
and cannot be passed to Static Maps. No mapping between the two is needed after all — the legacy
schema has its own building type.

**`maptype=roadmap`, not `terrain`**: specs/042 deliberately chose terrain *because* it does not
render buildings. This feature needs the opposite, for the same reason.

**The stated time budget FR-004 requires** *(added after `/speckit-analyze` found it unstated)* is
**30 seconds**, inherited unchanged from the existing `GoogleStaticMaps` named `HttpClient`'s own
`client.Timeout`. No new timeout is introduced; reusing the client (research decision, this
document's own opening rationale) means its budget comes along automatically. This is the number
quickstart Scenario 10 should assert against rather than deferring entirely.

---

## D2 — Vectorising every component, not the largest

**Decision**: add an all-components entry point to the existing `MaskContourVectorizer` and reuse
everything else it already does.

**Rationale**: the existing helper already performs, in order: flood-fill connected-component
labelling, exact pixel-grid edge tracing, Douglas-Peucker simplification, and pixel→geo conversion
through `StaticMapFraming`'s computed bounds. Every one of those steps is identical for buildings.
The *only* difference is that a site has one boundary and a neighbourhood has many, so
`LargestComponent` becomes "every component above a minimum size".

This is genuinely additive: `TryExtractRing` (existing, single largest) is untouched, and its two
current callers — the boundary extractor and the Gemini segmentation path — are unaffected.

**Alternative rejected**: a separate buildings-specific vectorizer. It would duplicate tracing and
simplification, which is exactly the business-logic duplication §2.III forbids, and would drift
from the boundary path's own hard-won fixes (the doc comment on that file records a morphological
closing pass that was added, then removed once it was found to chamfer real corners).

---

## D3 — Positional tolerance, derived from resolution rather than chosen

**Decision**: state the tolerance as **±1 metre** at the working zoom, and fetch at **zoom 18 with
`scale=2`**.

**Derivation** — Web Mercator ground resolution at each candidate zoom:

| Zoom | Dubai (25.2°N) | Cairo (30.1°N) | Tile covers (640px @ scale=2) |
|---|---|---|---|
| 17 | 0.541 m/px | 0.516 m/px | ~690 m |
| **18** | **0.270 m/px** | **0.258 m/px** | **~345 m** |
| 19 | 0.135 m/px | 0.129 m/px | ~170 m |

Zoom 18 at `scale=2` gives ~0.27 m/pixel and a tile covering ~345 m — comfortably more than the
200 m default analysis diameter, so **one tile suffices** and no stitching is needed (unlike the
boundary path, which stitches four tiles for large sites).

The error budget is then: ~1 pixel of threshold edge (0.27 m) plus Douglas-Peucker's 3-pixel
tolerance (0.81 m) ≈ **1.08 m**, so ±1 m is the honest stated figure — inherited from the image's
own resolution, exactly as specs/052's solar tolerance was inherited from its algorithm rather than
invented.

**Zoom 19 rejected**: halves the error but covers only ~170 m, forcing a multi-tile stitch for the
default radius. Not worth it — 1 m is already far below the accuracy of the *heights* the shadows
are computed from.

---

## D4 — 4-connected flood fill for buildings, not 8-connected

**Decision**: trace building components with **4-connectivity**, while the existing boundary path
keeps its 8-connectivity.

**Rationale**: this is the concrete answer to FR-010 (touching buildings). The existing
`LargestComponent` uses 8-connected flood fill, which merges any two blobs that touch *even
diagonally* — correct for a single site boundary, where bridging a one-pixel gap is desirable.
For buildings it is actively harmful: dense terraced rows would merge into one enormous footprint
casting one enormous wrong shadow.

4-connectivity only merges blobs sharing a full pixel edge, which keeps diagonally-adjacent
buildings separate. Buildings that genuinely share a wall in the rendering will still merge — and
that limitation is **stated rather than papered over** (FR-010 permits either separation by a rule
or a stated limitation). No morphological erosion is attempted to split them: the boundary path's
own history records that a square structuring element chamfers every non-grid-aligned corner, which
would damage far more footprints than it separates.

---

## D5 — Minimum area in square metres, not pixels

**Decision**: discard components below a stated **minimum ground area of 15 m²**, converted to a
pixel count using the tile's own metres-per-pixel.

**Rationale**: FR-008 requires a stated minimum. The existing vectorizer's noise threshold is
`0.15 × image diagonal` — tuned for "a site boundary spans most of the frame" and wildly wrong
here; at that threshold nearly every real building would be discarded. Expressing the floor in m²
makes it meaningful and zoom-independent: 15 m² is smaller than any habitable structure but larger
than the specks and marker remnants that survive thresholding. At zoom 18 that is roughly 200
pixels, comfortably above tracing noise.

---

## D6 — Buildings crossing the analysis boundary are kept whole

**Decision**: a building whose footprint is partly outside the requested radius is **included in
full** if any part of it falls inside. Buildings clipped by the *tile edge* are discarded.

**Rationale**: the spec's edge case requires a stated rule, and the shadow consequence decides it.
A truncated building casts a shadow the real building would not — the error is in the output the
user actually looks at. Including a partly-outside building costs nothing, since it is real and its
shadow is real.

The tile edge is different: there the geometry is genuinely unknown beyond the frame, so a clipped
footprint is a fabrication. Discarding it is safe precisely because D3 sizes the tile (~345 m) well
beyond the analysis radius (200 m), so tile-edge clipping only ever affects buildings far outside
the area of interest.

---

## D7 — Source arbitration: primary wins outright, never merged

**Decision**: a `CompositeBuildingFootprintProvider` decorator tries the rendered provider first
and falls back to Overpass **only when the rendered provider returns no footprints or fails**.
Results are never combined.

**Rationale**: FR-014 forbids merging, and the reason is geometric rather than stylistic — the two
sources derive footprints independently and their polygons do not coincide, so a merged result
would render every building twice, slightly offset, each casting its own shadow. That is the same
"ghost duplicate" failure specs/052 already solved once (its research D13).

Whole-result arbitration, not per-building: picking per building would require matching footprints
across sources, which is the same alignment problem in a harder form.

**Which source supplied a result is recorded** (FR-015) on the result envelope, so a coverage gap
is diagnosable rather than invisible.

**Alternative rejected**: rendered-only, dropping Overpass. It would trade one single point of
failure for another, and the two fail for unrelated reasons — the rendered source for coverage or
style-schema reasons, Overpass for load. Keeping both is the whole value.

---

## D8 — Heights stay exactly where they are

**Decision**: `RenderedBuildingFootprintProvider` returns `heightProvenance: 'assumed'` and the
stated default for every footprint. Enriching from OSM tags is **not** attempted in this feature.

**Rationale, stated plainly because it is a real trade-off**: rendered imagery carries no height
data, and matching a rendered footprint to an OSM building to borrow its height tag is a spatial
join across two independently-derived geometries — the same alignment problem D7 avoids. Doing it
badly would attach one building's height to another's outline, which is worse than an honest
default because the user is told it is a recorded height.

So this feature trades *height fidelity* for *footprint coverage* wherever the rendered source
wins. FR-016 to FR-018 still hold — heights remain independently sourced, the default is stated,
and provenance is still shown — but a user at a site served by the rendered source will see more
"assumed" heights than one served by Overpass.

This is worth accepting because the alternative today is **no buildings at all** in those places,
and because specs/052 already gives the user a correction control for exactly this (its FR-025).
The cleanest future fix is a dedicated height provider behind its own seam, which spec.md's
Downstream section already records.

---

## D9 — Caching lives inside each inner provider, not around the composite

**Decision** *(corrected after `/speckit-analyze`)*: `RenderedBuildingFootprintProvider` caches its
own results internally, exactly the way `OverpassBuildingFootprintProvider` already does — not
through a wrapper around the composite.

**Why the original plan was wrong**: the first draft of this plan assumed caching was "the existing
`IMemoryCache` registration... already uses for building retrieval" and that the composite would
inherit it for free. Reading the actual code shows this is false: `OverpassBuildingFootprintProvider.SearchAsync`
caches **inside its own method body**, keyed under its own `Buildings:Overpass` options section —
there is no external wrapper anywhere to inherit. Wiring the composite in as `IBuildingFootprintProvider`
without fixing this would leave the new *primary* source — the rendered one — with **zero caching**,
so every solar-analysis open/close would re-fetch and re-vectorise a billed Static Maps tile.

**The fix**: give `RenderedBuildingFootprintProvider` the identical pattern — check cache, fetch and
vectorise on a miss, store with a stated TTL — under its own `Buildings:Rendered` section
(`RenderedFootprintOptions.CacheTtl`, default 15 minutes, matching Overpass's). Each inner provider
owns its own cache; the composite wraps neither and needs no cache of its own, since a cache hit on
either inner provider already short-circuits the fetch it would otherwise perform.

**Alternative rejected**: a single cache wrapping the composite. It sounds cleaner, but it would
cache the *arbitration outcome* rather than each source's own result, so a request that fell back
to Overpass on a first (rendered-cold) call would keep hitting Overpass on every subsequent call
even after the rendered cache would have been warm — the composite has no way to know that without
re-querying rendered anyway, defeating the point of caching it.

---

## Resolved unknowns

| Unknown | Resolution |
|---|---|
| Does the styling isolate buildings? | D1 — verified live at two sites; `landscape.man_made.building` filled |
| Cloud→legacy schema mapping | D1 — not needed; the legacy schema has its own building type |
| How to get many polygons, not one | D2 — additive all-components entry point on the existing vectorizer |
| Stated positional tolerance | D3 — ±1 m, derived from 0.27 m/px at zoom 18 scale 2 |
| Touching buildings (FR-010) | D4 — 4-connected fill; shared-wall merging stated as a limitation |
| Minimum usable size (FR-008) | D5 — 15 m² ground area, converted per-tile |
| Buildings crossing the boundary | D6 — kept whole inside the radius; discarded at the tile edge |
| Source arbitration (FR-014) | D7 — primary wins outright, never merged, source recorded |
| Heights for rendered footprints | D8 — stated default, marked assumed; no cross-source matching |
| Where caching actually lives | D9 — inside each inner provider, not around the composite |
| Stated time budget (FR-004) | D1 addendum — 30s, inherited from the reused HTTP client |
