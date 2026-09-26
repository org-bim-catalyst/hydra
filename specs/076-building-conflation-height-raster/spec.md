# SPEC-076: Parallel Footprint Conflation, Height Raster and a Dome that Fits the Site

**Status:** Implemented 2026-09-26
**Depends on:** specs/052-solar-analysis, specs/053-rendered-building-footprints, specs/075-building-heights-esri-overture

## Problem

After specs/075, shadows at BurJuman still made no sense:

1. **One Esri feature, two buildings.** Esri models BurJuman's 25 m mall and its ~100 m tower as
   one feature, and its only height attribute is the tower top. specs/075 took one height per
   building, so the whole mall was drawn 105 m tall.
2. **First source wins.** The chain stopped at the first non-empty source. Rendered imagery found
   14 buildings around BurJuman while Overture had 70, and those were never used.
3. **The dome.** The sun-path dome is a fixed 120 m. The mall is about 200 m across, so it pokes
   through the dome and the picture reads as wrong.

## Decision

1. **Heights are a raster.** `IBuildingHeightSource` returns a `BuildingHeightMap`: 2 m cells
   holding the roof height above that feature's ground, or `NaN`. `RoofHeightRasterizer` draws each
   measured feature's roof triangles into it.
2. **Enrichment reads the raster.** An assumed footprint with at least half its cells measured
   takes the median and becomes Known. Taller parts become extra Known casters `{id}_part{n}`, up
   to three setbacks deep. Measured roofs that no footprint claims become `esri_{n}` buildings.
3. **All footprint sources run in parallel and are conflated.** Priority stays rendered →
   Overture → OSM. The primary is kept whole. A lower source's footprint is drawn only where it
   fills a gap, meaning under 30 % overlap. A counterpart that covers at least half of a kept
   assumed footprint passes on its height. The site building is resolved across sources.
4. **The dome grows to enclose the building under study.** Its radius is
   `max(120 m, 1.15 × furthest roof corner)`, rounded up to 10 m and capped at 600 m. "Building
   under study" means the site building plus its `_part` casters. The dome is display-only.
   Shadows come from the directional light, which is sized by the content bounds, so the dome's
   size never enters the calculation.
5. **Attribution goes on the Terms page only (section 8), not on screen.** It credits Google,
   OpenStreetMap contributors (ODbL), the Overture Maps Foundation, and "Esri, Vantor". See
   docs/THIRD_PARTY_NOTICES.md.

## Behaviour changes

- **Empty is an answer.** Before, if one source answered empty and the last source failed, the
  request returned a 503. Now it returns an empty result with `Source = None`. A 503 is returned
  only when every source fails.
- **Lower sources get a time limit.** They get `Buildings:Conflation:StragglerBudget` (8 s) after
  the primary answers. After that they are cancelled and logged.

## Configuration

| Key | Default | Meaning |
|-----|---------|---------|
| `Buildings:Conflation:StragglerBudget` | `00:00:08` | How long lower-priority sources may run after the primary answers |
| `Buildings:Esri:HeightMapCellMetres` | `2` | Height raster cell size |

No `appsettings.json` change is required; all defaults live in code.

## Verified live (2026-09-26)

- **BurJuman:** 89 buildings. 14 are rendered, 56 were gap-filled from Overture and 19 are tower
  parts; 73 are Known.
  - Mall footprints are 26 m and 22.5 m. The tower parts are 84.5 m and 98.1 m.
  - Warm latency is 2.5 s (9.3 s cold).
- **Al Safa (villas):** 223 buildings, 98 Known. Villa heights match typical one- to two-storey
  villas.

## Operational note

ArcGIS reports an expired or rejected API key as **HTTP 200** with a JSON `error` body
(498 "Token would have expired"). The layer is public, so the source drops the key, retries
anonymously and remembers the rejection. The configured `Esri:ApiKey` should be renewed or removed.

## Tests

- `HeightEnrichingBuildingFootprintProviderTests`: mall and tower, setbacks, coverage, never
  overwrite Known, no ghost from a 4 m offset, gap-fill, limit, and failure paths.
- `CompositeBuildingFootprintProviderTests`: parallel sources, gap-fill without double-drawing,
  height transfer, site resolution, straggler budget, and empty versus all-failed.
- `EsriBuildingHeightSourceTests`: per-feature ground, placeholders dropped, rejected key.
- Frontend: `footprintGeometry.test.ts` checks the site reach. `sunPathCurve.test.ts` checks that
  the dome radius fits the site building and redraws at once, and that the shadow rig is
  unchanged.
