# Contract: Footprint Source Arbitration

`CompositeBuildingFootprintProvider` — the registered `IBuildingFootprintProvider`. It owns two
inner providers and decides between them; it performs no retrieval of its own.

```csharp
CompositeBuildingFootprintProvider(
    [FromKeyedServices("rendered")] IBuildingFootprintProvider primary,
    [FromKeyedServices("osm")] IBuildingFootprintProvider fallback,
    ILogger<CompositeBuildingFootprintProvider> logger)
```

**Corrected during implementation.** This originally took the two concrete provider types directly
(`RenderedBuildingFootprintProvider primary, OverpassBuildingFootprintProvider fallback`). Both are
`sealed`, and NSubstitute cannot proxy a sealed class — a concrete-type constructor could never
actually be unit-tested. Depending on the interface instead, disambiguated by keyed DI, is both
fully fakeable and a better dependency direction (constitution §2.V): this class no longer needs to
know either concrete provider type exists, only that two keyed registrations resolve to something
implementing `IBuildingFootprintProvider`.

A decorator rather than an `if` inside one provider: each source stays single-purpose (§2.II SRP),
and a third source later is an added implementation rather than an edit to working code (OCP).

## The rule

```text
primary.SearchAsync
   │
   ├─ returned ≥ 1 footprint ──────────────▶ return it,  Source = rendered
   │
   ├─ returned 0 footprints ───────┐
   └─ threw unavailable ───────────┤
                                   ▼
                          fallback.SearchAsync
                                   │
                                   ├─ returned ≥ 1 ─────▶ return it, Source = osm
                                   ├─ returned 0 ───────▶ empty,     Source = none
                                   └─ threw ────────────▶ rethrow
```

### Guarantees

- **Results are never merged** (FR-014). The two sources derive their geometry independently and
  their polygons do not coincide, so a combined result would draw every building twice, slightly
  offset, each casting its own shadow — the same "ghost duplicate" failure specs/052 already solved
  once (its research D13). Whole-result arbitration also avoids matching footprints across sources,
  which is that same alignment problem in a harder form.
- **A failing primary is not a feature failure** (FR-012). It is the ordinary path to the fallback,
  and produces no user-visible error.
- **Both failing rethrows**, so the existing `503` Problem Details mapping still applies unchanged
  (FR-013). The user sees "building data is unavailable" and, per specs/052 FR-014, the sun path
  keeps working.
- **Both empty is a success**, not an exception — `Source = none`, rendered by specs/052 as its
  existing "no buildings found" notice.
- **Which source was used is recorded on the result and logged** (FR-015), so a coverage gap is
  diagnosable rather than invisible. This matters more than it looks: silent fallback would hide
  the rendered source degrading in a whole region.

### Ordering rationale

Rendered is primary because it is the source that **works where this platform's users are**. Three
published alternatives were evaluated and all three exclude the Gulf (spec.md Downstream); the
rendered basemap is the only Google source tested that covers Dubai, because it reads what Google
draws everywhere rather than a derived dataset with a coverage frontier.

Overpass is kept rather than dropped because the two fail for **unrelated reasons** — rendered for
styling or coverage reasons, Overpass for load — which is precisely what makes the pair worth more
than either alone. Replacing one single point of failure with another would be no gain.

### Caching

**Corrected after `/speckit-analyze`, which found the original claim below did not match the
actual code.** ~~Caching stays where specs/052 put it, wrapping the composite rather than either
inner provider~~ — this assumed a shared wrapper that does not exist: `OverpassBuildingFootprintProvider`
has always cached **inside its own `SearchAsync`**, keyed to its own options section, with nothing
external to inherit.

**The composite wraps neither provider and holds no cache of its own.** Each inner provider caches
its own result independently (research D9) — `RenderedBuildingFootprintProvider` gains the
identical internal pattern under `Buildings:Rendered`. This is deliberately *not* a single cache
keyed on the arbitration outcome: doing that would mean a request that fell back to Overpass once
(because the rendered cache was cold) keeps falling back on every later call too, since the
composite would have no way to know the rendered cache had since warmed up without querying it
anyway — which defeats caching it at all.

## Registration

`DependencyInjection.cs` registers the composite as `IBuildingFootprintProvider`; both inner
providers are registered as **keyed** `IBuildingFootprintProvider` implementations (`"rendered"`,
`"osm"` — corrected during implementation, see above). specs/052's registration line changes from
binding the interface directly to `OverpassBuildingFootprintProvider` to binding it to the
composite — **the only change to existing backend wiring in this feature.**
