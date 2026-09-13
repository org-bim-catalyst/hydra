# Implementation Plan: Building Footprints from Rendered Map Imagery

**Branch**: `053-rendered-building-footprints` | **Date**: 2026-09-13 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/053-rendered-building-footprints/spec.md`

## Summary

Building footprints come from the map provider's own rendering instead of OpenStreetMap, using the
same styled-static-map technique specs/042 already uses for site boundaries — and reusing most of
its code, not just its idea.

The pipeline is: request a Static Maps tile styled so buildings are the only thing drawn, threshold
the magenta pixels into a binary mask, and vectorise **every** connected component into a polygon.
The existing `MaskContourVectorizer` already does flood-fill component finding, pixel-grid edge
tracing, Douglas-Peucker simplification and pixel→geo conversion; it simply returns the *largest*
component because a site has one boundary. This feature adds an all-components entry point beside
that one and changes nothing about the existing path.

Footprints become the primary source, with the existing Overpass provider kept as fallback behind
the `IBuildingFootprintProvider` interface specs/052 already defined — so solar analysis sees no
change at all. Heights continue to come from OSM tags, entirely independently of where the outline
came from.

**The styling is already verified live** (two locations, including one where OSM has no data),
which is why Phase 0 spends its budget on the conversion step instead.

## Technical Context

**Language/Version**: C# / .NET 10 — this feature is entirely backend. No frontend change.

**Primary Dependencies**: Existing only — `SixLabors.ImageSharp` 3.1.12 (already used for boundary
raster work), the existing `GoogleStaticMaps` named `HttpClient`, MediatR, FluentValidation. **No
new package.**

**Storage**: None. No schema change, no migration. **Corrected after `/speckit-analyze`**: caching
is not a shared wrapper — each provider (Overpass today, the rendered one added here) caches its
own result internally in the existing `IMemoryCache`, the same pattern in two places rather than
one shared mechanism (research D9).

**Testing**: xUnit + NSubstitute + FluentAssertions. Raster fixtures checked in as small PNGs so
vectorisation is tested deterministically without network access.

**Target Platform**: ASP.NET Core on Windows/site4now hosting.

**Project Type**: Backend feature behind an existing interface. No new public contract to the
frontend — `GET /api/v1/site-buildings` keeps its current request and response shape.

**Performance Goals**: Retrieval within the existing budget for the endpoint. One tile fetch plus
vectorisation of a 1280×1280 mask; the existing boundary path already does the same work at the
same size, including a four-tile stitch, inside its budget.

**Constraints**: Static Maps returns footprints only — no height information of any kind exists in
a rendered image, so height sourcing is untouched. Positional accuracy is bounded by the image's
own resolution (see research D3). Provider credentials and third-party imagery stay server-side.

**Scale/Scope**: Same bounded radius and count cap specs/052 already applies (200 m default, 300
buildings). 3 user stories, 22 functional requirements, 8 success criteria.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Pre-design evaluation — PASS.**

| Principle | Assessment |
|---|---|
| §2.I Clean Architecture | New code is one `Infrastructure` implementation of an interface `Application` already owns (`IBuildingFootprintProvider`), plus a composing provider. Nothing in `Application` or `Domain` changes. No inward-pointing dependency. |
| §2.II SOLID | The fallback arrangement is a decorator over two providers, not an `if` inside one — each provider stays single-purpose (SRP), and a third source later is an added implementation, not an edit (OCP). |
| §2.III DRY/KISS/YAGNI | The whole point: framing, fetching, thresholding, tracing, simplification and pixel→geo conversion are **reused** from specs/042 rather than re-derived. No new package. No abstraction for a third source that does not exist. |
| §2.V DI & testability | Providers injected; the composing provider is unit-testable with two fakes and no network. Vectorisation is a pure function over a mask, testable from a checked-in PNG. |
| §2.VI Separation of concerns | Fetching (Infrastructure), vectorising (a pure static helper), arbitration (a provider decorator) and height merging stay distinct. |
| §2.VII Convention over configuration | Mirrors `GoogleRenderedFillBoundaryExtractor` exactly — same client, same framing helper, same vectorizer, same options-binding shape. |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | FR-021/FR-022 and SC-007 restate this at feature level. Every path — tile fetch failed, nothing rendered, mask unusable, both sources empty — resolves to a typed outcome the caller surfaces; specs/052's UI already renders that as its `partial` state with a stated notice. A failing primary source is not an error at all, it is a fallback. |
| §8 Security | No user input reaches the style string — it is a fixed constant, and the only interpolated values are already-validated coordinates and zoom. Imagery and API key stay server-side. Third-party geometry remains untrusted data crossing a boundary we control. |
| §10 Testing | Vectorisation tested against checked-in raster fixtures; arbitration tested with faked providers; the provider tested against a recorded response, per §10's "recorded/replayed responses" rule for Infrastructure adapters. |
| §15 Performance | One tile fetch per retrieval, cached. Vectorisation is O(pixels) flood fill over a mask the same size the boundary path already handles. |

**No violations. Complexity Tracking table is empty.**

**Post-design re-evaluation (after Phase 1) — PASS, with one correction to the pre-design claim
above.** The design adds one Infrastructure implementation, one composing provider, one options
type and one additive entry point on an existing internal helper. No new datastore and no new
cross-cutting pattern, so no ADR is required under §17.

**Correction**: the pre-design table said `Application` does not change. Phase 1 found it must, by
exactly one additive field — `BuildingFootprintResult.Source` (`rendered` | `osm` | `none`), which
FR-015 requires so a coverage gap is diagnosable rather than invisible. A structured log line would
technically satisfy "recorded for diagnosis" and preserve a spotless untouched-`Application`
claim, but it would put the answer somewhere no caller can act on it, and choosing the weaker
design to protect a tidier claim is the wrong trade. The field is optional with a default, so
`IBuildingFootprintProvider`, the endpoint route, and the response shape every existing consumer
reads are all still unchanged — which is what FR-019/SC-008 actually assert.

## Project Structure

### Documentation (this feature)

```text
specs/053-rendered-building-footprints/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── rendered-footprint-provider.md
│   └── footprint-source-arbitration.md
├── checklists/
│   └── requirements.md  # Already complete
└── tasks.md             # Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
src/AskLucy.Infrastructure/Buildings/
├── RenderedBuildingFootprintProvider.cs      # NEW — styled tile → polygons
├── RenderedFootprintOptions.cs               # NEW — style, zoom, min area, tolerance
├── CompositeBuildingFootprintProvider.cs     # NEW — primary + fallback arbitration
├── OverpassBuildingFootprintProvider.cs      # UNCHANGED — becomes the fallback
└── BuildingRetrievalOptions.cs               # UNCHANGED

src/AskLucy.Infrastructure/Boundaries/
├── MaskContourVectorizer.cs                  # MODIFIED — additive all-components entry point
└── StaticMapFraming.cs                       # UNCHANGED — reused as-is

src/AskLucy.Infrastructure/DependencyInjection.cs   # MODIFIED — registration order

tests/AskLucy.Infrastructure.Tests/
├── Buildings/RenderedBuildingFootprintProviderTests.cs      # NEW
├── Buildings/CompositeBuildingFootprintProviderTests.cs     # NEW
├── Boundaries/MaskContourVectorizerAllComponentsTests.cs    # NEW
└── Buildings/fixtures/*.png                                 # NEW — checked-in masks
```

**Structure Decision**: Everything lives in `Infrastructure`, because that is the only layer that
changes. `Application` already owns `IBuildingFootprintProvider` (specs/052) and its shape is
sufficient — which is the strongest evidence the seam was drawn in the right place. The vectorizer
stays in `Boundaries/` rather than moving: it is already shared by three different mask producers,
its doc comment says so, and relocating shared code to follow its newest caller would be churn.
`Application` and `Domain` are untouched.

## Complexity Tracking

> No Constitution Check violations. Table intentionally empty.
