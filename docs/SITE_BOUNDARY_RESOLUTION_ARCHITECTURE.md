# Site Boundary Resolution — Architecture Proposal

> **Status:** SUPERSEDED — implemented as `specs/042-site-boundary-resolution/`. This document
> was the pre-implementation proposal; several of its central claims turned out to be wrong once the
> actual codebase wiring was traced during implementation and, later, once real production usage
> surfaced bugs the proposal hadn't anticipated. This file was **not** rewritten to match (kept as
> a historical record of the proposal, not as current documentation). For the accurate, implemented
> design, read `specs/042-site-boundary-resolution/research.md` (especially #10-11), `data-model.md`,
> `contracts/`, and `tasks.md` instead of this file.
>
> **Then read `specs/044-location-viewer-regression/`.** Parts of 042's own contracts have since
> been superseded too. 042 placed boundary resolution *between* resolving a location and delivering
> it to the viewer, with neither a failure boundary nor a time boundary — so an optional enhancement
> became a hard prerequisite for a mandatory outcome, and "Show me Al Safa Park 2" stopped updating
> the viewer whenever the boundary step threw or hung. 044 restores the pre-042 property that no
> network call sits between resolving a location and delivering it, caps the whole boundary step at
> 45s, isolates its failures, and clears stored boundary state when the active site changes. Its
> `contracts/chat-stream-events.md` is the current authority on SSE ordering.
>
> **The corrections:**
> 1. **§5's `SiteBoundaryResolverTool : IAgentTool` is NOT the primary mechanism.** The feature's
>    base-chat behavior (a user just asking Lucy about a site) is driven by a deterministic hook
>    inside `SendChatMessageCommandHandler`/`AiController` — the same seam `ILocationResolutionService`
>    already uses (`ConfirmedLocationData` → `RecordActiveLocationCommand` → `__LOCATION__` SSE
>    event, mirrored here as `ConfirmedSiteBoundaryData` → `RecordActiveSiteBoundaryCommand` →
>    `__SITE_BOUNDARY__`). The `IAgentTool` was built too, but only as a secondary surface for
>    custom user-authored agents.
> 2. **§8's "no persistence" call is wrong.** `ActiveSiteLocation` (this doc's own analogy target)
>    turned out to already be a real, persisted owned-value column on `UserChat`, not in-memory —
>    so `ActiveSiteBoundary` needed the identical treatment: new nullable columns on `UserChats`
>    via a real EF Core migration, not "no persistence."
> 3. **The primary viewer rendering path is a native `google.maps.Polygon`, not the Three.js/
>    WebGLOverlayView bridge described in §7.** That bridge's own code comment admits it was never
>    runtime-verified in a live browser, and a live production test proved it out: nothing rendered.
>    `GoogleMapsGisLayer.setSiteBoundary` now draws a real `google.maps.Polygon` directly on the map
>    as the guaranteed-visible path; the Three.js comet-highlight effect (§7) still runs alongside it
>    as a bonus visual, wrapped in try/catch so a renderer failure there can never blank the boundary.
> 4. **§2's "AI vision step is pluggable and optional, off by default" needs a footnote.** It's
>    implemented almost verbatim from the notebook's `ai_boundary_analysis`/`select_final_candidate`
>    (`IBoundaryVisionAnalyzer`/`GeminiBoundaryVisionAnalyzer`, `ISatelliteImageProvider`/
>    `GoogleSatelliteImageProvider`, reconciliation logic in `BoundaryResolutionService`; later joined
>    by `IStreetViewImageProvider`/`GoogleStreetViewImageProvider` for a ground-level cross-check —
>    see `LOCATION_TO_BOUNDARY_END_TO_END.md` §9.6. `ISatelliteImageProvider`'s implementation now
>    renders Google's roadmap layer, not satellite photography, and Gemini's traced outline is
>    adopted as the final polygon outright rather than only steering a translation — see §9.7), but
>    defaults
>    to **enabled** (`BoundaryScoring:EnableAiVisionVerification`), not disabled — a live bug (Al Safa
>    Park 2 resolving to a small sub-feature inside the park instead of the park itself) showed that
>    deterministic tag/geometry scoring alone isn't always enough to disambiguate similarly-plausible
>    OSM candidates, and a missing Gemini credential already degrades this gracefully to the
>    deterministic score alone (`BoundaryVisionAnalysis.NotConfigured`) rather than erroring. That
>    same bug also required restricting the OSM tag filters (`OverpassBoundaryCandidateProvider`) to
>    the notebook's own curated `leisure`/`amenity` values instead of a blanket "any value" scan, and
>    crediting an OSM `name:en` tag so a non-Latin-script primary name (e.g. Arabic) can still match a
>    Latin-script query.
>
> **Source material:** `docs/AL_SAFA_PARK_2_AI_ANALYSIS_V5.ipynb` (Module 01, cells 6–35) and `docs/BORDER_HIGHLIGHT.html`
> **Related modules:** `specs/037-location-query-resolution` (point resolution), `specs/027-immersive-viewer-platform` (Three.js viewer), `specs/042-site-boundary-resolution` (the implemented spec)

## 1. Purpose

Give Lucy a general capability to take a site/location reference, resolve it to a **polygon boundary** (not just a point), attach a **confidence score and source**, and render it in the Three.js viewer with a highlighted border — starting with Al Safa Park 2, but built so any future urban-planning/design project can reuse it without touching the core pipeline.

This document does **not** cover the notebook's downstream analysis (data-layer collection, KPI scoring, design-concept generation, cost modeling). It scopes only: *identify → bound → polygonize → display → explain confidence/source*, per the request.

## 2. Design principles carried over from the notebook

These aren't stylistic choices — they're the reason the notebook's resolver is trustworthy, and they should carry over exactly:

- **Source-first, not AI-first.** Real GIS/OSM polygons are always preferred over anything AI-generated. AI is a *tie-breaker/critic* over existing candidates, never a coordinate generator.
- **Never auto-accept low confidence.** A low-confidence result is surfaced, never silently applied. This maps directly onto Ask Lucy's global "no silent failures" rule (`CLAUDE.md` → Error Handling) — a low-confidence boundary rendered as if it were certain *is* a silent failure.
- **Confidence and source are always explainable**, not just a bare number — every result carries a human-readable trail (which provider, which candidate, why it beat the alternatives, what's uncertain about it).
- **The AI vision step is pluggable and optional**, off by default, and degrades to a clearly-labeled "not evaluated" result rather than faking analysis when no vision-capable path is wired in.
- **Weights/thresholds are configuration, not hard-coded constants**, and are validated to sum to 1.0 — so tuning for a different project/typology doesn't require touching the scoring function.

## 3. Why extend `Locations`, not build a parallel stack

The codebase already has a direct analog: `specs/037-location-query-resolution`.

- `AskLucy.Domain.Chats.ActiveSiteLocation` already carries `(Latitude, Longitude, LocationName, Confidence)`.
- `ILocationResolutionService` / `LocationResolutionService` already does intent classification → geocoding → a confidence/dominance-margin algorithm → WGS-84 validation → a typed outcome, using a swappable `IGeocodingProvider` (`GoogleMapsGeocodingProvider` / `NominatimGeocodingProvider`).

Boundary resolution is the natural next step after point resolution, not a separate concern: **first resolve the point (reuse `ILocationResolutionService` as-is), then resolve the polygon around that point.** This avoids re-implementing geocoding, intent classification, or confidence plumbing, and keeps one place responsible for "where is this thing" while a new, narrower module owns "what shape is it."

No polygon/boundary/GeoJSON concept exists anywhere else in the codebase today (confirmed by search) — there is nothing to conflict with or accidentally duplicate.

## 4. Domain model — `AskLucy.Domain.SiteBoundaries`

Flat files, following the `Locations`/`Workflows` convention (no sub-folders):

```csharp
// SiteBoundaryPolygon.cs — a plain WGS84 ring; no new geometry-library dependency (see §7.3)
public sealed record SiteBoundaryPolygon(IReadOnlyList<GeoPoint> ExteriorRing);
public sealed record GeoPoint(double Latitude, double Longitude);

// SiteBoundarySource.cs
public enum SiteBoundarySource
{
    OsmBoundary,
    GovernmentCadastral,   // reserved for a future authoritative provider
    AiInterpretation,
    UploadedBoundary,
    ManualFallback,        // circular buffer around a point — today's only fallback everywhere
}

// BoundaryConfidenceLevel.cs
public enum BoundaryConfidenceLevel { Low, Medium, High }

// SiteBoundaryResult.cs — the thing everything downstream (viewer, agent tool, chat) consumes
public sealed record SiteBoundaryResult(
    string SiteName,
    GeoPoint Centroid,
    SiteBoundaryPolygon Polygon,
    double AreaSquareMeters,
    double Confidence,                 // 0.0–1.0, combined score
    BoundaryConfidenceLevel ConfidenceLevel,
    SiteBoundarySource Source,
    string SourceDetail,               // e.g. "OpenStreetMap way 123456 (leisure=park)"
    IReadOnlyList<string> Notes);       // explainability trail — always populated, never empty
```

No entity/persistence is proposed for v1 — see §8.

## 5. Application layer — `AskLucy.Application.SiteBoundaries`

### 5.1 Candidate provider abstraction

```csharp
public interface IBoundaryCandidateProvider
{
    Task<IReadOnlyList<BoundaryCandidate>> SearchAsync(
        GeoPoint center, int radiusMeters, CancellationToken cancellationToken);
}

public sealed record BoundaryCandidate(
    string Id,
    SiteBoundaryPolygon Polygon,
    SiteBoundarySource Source,
    string Name,
    IReadOnlyDictionary<string, string> Tags,
    double DistanceToCenterMeters,
    double AreaSquareMeters);
```

Mirrors `IGeocodingProvider` exactly — same shape, same reason (swappable, testable, provider-agnostic).

### 5.2 Scoring — `BoundaryScoringOptions` + `BoundaryCandidateScorer`

Direct port of the notebook's `SITE_BOUNDARY_CONFIG` / `score_candidate`:

```csharp
public sealed class BoundaryScoringOptions
{
    public int SearchRadiusMeters { get; init; } = 500;
    public int MaxCandidates { get; init; } = 10;
    public double HighConfidenceThreshold { get; init; } = 0.85;
    public double MediumConfidenceThreshold { get; init; } = 0.65;

    // Must sum to 1.0 — validated at startup (options validation), same as the
    // notebook's `assert abs(sum(weights) - 1.0) < 1e-6`.
    public double SourceReliabilityWeight { get; init; } = 0.35;
    public double NameMatchWeight { get; init; } = 0.20;
    public double GeometryQualityWeight { get; init; } = 0.15;
    public double CenterProximityWeight { get; init; } = 0.20;
    public double LandUseAgreementWeight { get; init; } = 0.10;
}
```

Bound from `appsettings.json` via `IOptions<BoundaryScoringOptions>` with a `IValidateOptions` implementation enforcing the sum-to-1.0 invariant — this is what makes weights "configuration, not code" per §2, and lets a future non-park typology (e.g. a plaza, a campus) retune emphasis without a code change.

`SourceReliability` per `SiteBoundarySource` is a small static table (OSM=0.80, GovernmentCadastral=1.00, AiInterpretation=0.55, etc.) — same illustrative-ranking idea as the notebook's `SOURCE_RELIABILITY`.

### 5.3 Orchestration — `BoundaryResolutionService`

```csharp
public interface IBoundaryResolutionService
{
    Task<BoundaryResolutionOutcome> ResolveAsync(
        string locationQuery, Guid userChatId, CancellationToken cancellationToken);
}
```

Steps (mirrors the notebook 1:1, minus the DXF override which stays project-specific — see §10):

1. Resolve the point via the **existing** `ILocationResolutionService` (reuse, don't reimplement).
2. `IBoundaryCandidateProvider.SearchAsync` around that point.
3. Normalize + validate geometry (drop degenerate/self-intersecting candidates).
4. Score every candidate via `BoundaryCandidateScorer` using `BoundaryScoringOptions`.
5. *(Optional, off by default — see §7.2)* run an AI vision critique over the ranked candidates; reconcile agreement/override exactly as the notebook's `select_final_candidate` does.
6. Classify confidence (`High`/`Medium`/`Low`) from the combined score.
7. Return a `BoundaryResolutionOutcome` — never throws into a chat/agent-tool caller (constitution §2.VIII, same discipline as `LocationResolutionService`): failures become an `Unavailable`/`NoCandidates` outcome with a caller-visible reason, never a swallowed exception.

`BoundaryResolutionOutcome` carries the winning `SiteBoundaryResult` **and** the full ranked candidate list + score breakdowns, so a caller (chat UI, agent tool) can show "here's what else was considered" — matching the notebook's map view that shows all candidates, not just the winner.

### 5.4 Agent Tool — `SiteBoundaryResolverTool`

New flat file in `AskLucy.Application/Agents/Tools/SiteBoundaryResolverTool.cs`, registered in `DependencyInjection.cs` alongside the other native tools — this is the reusable "kill" (tool) the user asked about, callable by any agent, not hardcoded to Al Safa 2.

- **Input schema:** `{ "locationQuery": string, "radiusMeters"?: number }`
- **Output schema:** the `SiteBoundaryResult` shape from §4, JSON-serialized — polygon as an array of `{lat, lng}` vertices, plus `confidence`, `confidenceLevel`, `source`, `sourceDetail`, `notes[]`.
- Delegates entirely to `IBoundaryResolutionService`; the tool class itself is a thin adapter (same idiom as `DocumentSearchTool`).
- `RiskLevel`: low/read-only (no writes, no side effects) — same tier as `DocumentSearchTool`/`KnowledgeSearchTool`.
- The tool's `notes[]` becomes the natural-language explanation the agent narrates back to the user ("I found this boundary via OpenStreetMap with high confidence — it matches the park's tagged area and sits within your search radius…"), satisfying "communicate confidence level and source" without a separate narration layer.

## 6. Confidence classification — kept identical to the notebook

| Combined score | Level | Behavior |
|---|---|---|
| ≥ 0.85 | High | Safe to render as the primary boundary; agent states it plainly. |
| 0.65–0.85 | Medium | Rendered, but the agent explicitly flags it as provisional and names what would raise confidence (e.g. "no official cadastral source available"). |
| < 0.65 | Low | Rendered only as a dashed/muted candidate outline, never as *the* answer — the agent asks the user to confirm or offers the top 2–3 alternatives, exactly like the notebook's `require_user_confirmation` gate. |

Agreement between the deterministic score and the (optional) AI vision pick is itself surfaced as one of the notes — "agree" boosts confidence slightly, "override" is flagged rather than silently swapped in, "ai_not_used" is stated plainly. This reconciliation logic (`select_final_candidate`) ports over unchanged in spirit.

## 7. Infrastructure layer — `AskLucy.Infrastructure.Boundaries`

### 7.1 `OverpassBoundaryCandidateProvider : IBoundaryCandidateProvider`

Queries the OSM Overpass API (free, no key — same source family as `NominatimGeocodingProvider`) for polygon-shaped features (leisure/landuse/building/boundary/natural tags) within the search radius. Same resilience posture as existing infra: timeouts, rate limiting, and a caught-and-mapped failure (never an unhandled exception reaching the service layer).

### 7.2 AI vision critique — deferred, narrow interface, not a global `IAIProvider` change

The notebook's Gemini-vision step needs to send image bytes to a model. Today, `IAIProvider`/`ChatMessage` is text-only across all four providers — no call site anywhere sends images. Widening the *core* chat abstraction to be multimodal for every provider is a separate, larger piece of work with its own blast radius (all four providers, every existing `ChatAsync` call site) and isn't needed for the boundary capability to work correctly — the notebook itself designed this step to be optional and to degrade gracefully.

Recommendation: introduce a **narrow, single-purpose interface** instead of touching `IAIProvider`:

```csharp
public interface IBoundaryVisionAnalyzer
{
    Task<BoundaryVisionAnalysis> AnalyzeAsync(
        byte[] satelliteImage, IReadOnlyList<BoundaryCandidate> candidates,
        GeoPoint center, CancellationToken cancellationToken);
}
```

Implemented only by a `GeminiBoundaryVisionAnalyzer` (Gemini already reports `Vision: true` capability metadata). `BoundaryResolutionService` takes `IBoundaryVisionAnalyzer?` as an **optional** dependency (null/no registration = feature off, same as the notebook's `enable_ai` flag) — this keeps the multimodal surface area contained to exactly the one place that needs it, and can be promoted into the shared `IAIProvider` contract later if other features want vision generally. **Proposed for a follow-up phase, not the v1 slice.**

### 7.3 Geometry utilities — no new package dependency

The repo has no GIS/geometry library today (no NetTopologySuite, no equivalent). Per "avoid unnecessary dependencies," a lightweight static `GeometryMath` helper (shoelace-formula area over an equirectangular local-meters projection around the centroid, centroid, bbox, distance) covers everything this capability needs — the same trade-off the notebook itself made for its non-geopandas fallback path. This is sufficient for scoring/plausibility checks and display; it is **not** survey-grade, and `SiteBoundaryResult` should say so in its notes when precision matters (mirrors the notebook's own DXF-override honesty notes).

## 8. Persistence — none for v1

`SiteBoundaryResult` is computed on demand and lives only in the response/chat turn, the same way `LocationResolutionOutcome` isn't persisted as its own table — only the lightweight `ActiveSiteLocation` point survives on `UserChat` for back-references. If a future need arises (e.g. "remember this boundary across sessions" or an audit trail of approved boundaries), that's an additive `SiteBoundaryRecord` entity + EF configuration + migration, deliberately deferred until there's a real requirement for it.

## 9. Frontend / viewer integration

### 9.1 State — `activeSiteBoundaryStore.ts` (Zustand)

New store mirroring `activeLocationStore.ts`: holds the current `SiteBoundaryResult` (polygon, confidence, confidenceLevel, source, sourceDetail) written whenever a chat turn's agent-tool response includes one. No new wiring pattern — reuses however `activeLocationStore` already gets populated from a chat response today.

### 9.2 Rendering — extend `GoogleMapsGisLayer`, not `createOverlay`

The viewer's `createOverlay` (`viewer/api/layers.ts`) is presently pure bookkeeping — nothing renders it. The real primitive to build on is `GoogleMapsGisLayer.ts`'s `WebGLOverlayView`: it already owns a live `THREE.Scene`/`THREE.PerspectiveCamera` and already converts `{lat, lng, altitude}` → scene space every frame via `transformer.fromLatLngAltitude(...)` (used today only to position the camera). That is exactly the primitive needed to place polygon vertices in 3D space.

Proposed new module: `viewer/layers/gis/SiteBoundaryRenderer.ts` — takes a `SiteBoundaryPolygon` + `GoogleMapsGisLayerHandle`, projects each vertex via the existing transformer, and adds/removes geometry from the shared scene. A new React component `features/viewer/components/SiteBoundaryOverlay.tsx` follows the exact `POIMarkerOverlay.tsx` idiom (imperative side-effect component, `return null`, cleans up on change) — subscribing to `activeSiteBoundaryStore` and calling the renderer.

### 9.3 Border highlight effect — generalized from `BORDER_HIGHLIGHT.html`

The reference file's technique — a dim static `THREE.Line` perimeter plus two additive-blended "shooting star" comets parameterized by arc-length (`getPointAtDistance`), rendered through `UnrealBloomPass` — becomes a small, reusable, **generic** module: `viewer/effects/AnimatedBorderHighlight.ts`. It should take *any* ordered point list (not just a rectangle) so it works for an arbitrary park boundary, a future plaza, a campus perimeter, etc. — this is the piece that most directly answers "keep it modular so it can be reused for other projects."

Confidence should modulate the effect rather than needing a separate legend graphic — e.g. comet color/intensity or perimeter opacity keyed off `confidenceLevel` (bright/saturated for High, softer for Medium, dashed/static-only — no comets — for Low, reinforcing "this is provisional" visually as well as textually). Exact palette to be decided against `DESIGN_SYSTEM.md` when this gets built, not invented ad hoc.

**Confirmed: the viewer's GIS layer runs no post-processing pipeline today.** `GoogleMapsGisLayer.ts` renders with a plain `renderer.render(scene, camera)` (no `EffectComposer`). The only bloom pipeline anywhere in the codebase is `features/chat/scene/ParticleSphereBloom.tsx`, which uses `@react-three/postprocessing`'s `SelectiveBloom` inside a react-three-fiber-managed canvas for the unrelated chat-avatar particle sphere — a different rendering stack from the GIS layer's imperative, Google-driven `WebGLOverlayView` loop, and not reachable from it as-is.

Adding a full `EffectComposer`/`UnrealBloomPass` to the map's live, shared GL context (shared with Google's own map tiles, driven by Google's per-frame `onDraw`) would be a real, invasive change to a performance-sensitive render path — not a drop-in. **Recommendation for v1: skip post-processing bloom entirely** and get the glow look from the shooting-star shader's own additive-blending + intensity math (`AdditiveBlending`, the head-brightening `intensity *= 0.25 + head * 3.0` term in `BORDER_HIGHLIGHT.html`) — this is where most of the visual punch already comes from in the reference file; `UnrealBloomPass` there is a polish layer on top, not the source of the effect. True bloom on the map layer can be revisited later as a scoped, deliberately-measured addition if the shader-only glow isn't enough.

### 9.4 Communicating confidence and source in the UI

Two channels, not one:

- **Narrative** — the agent tool's `notes[]` are what the assistant says in chat ("…found via OpenStreetMap, high confidence, matches the park's tagged boundary…"). No separate UI copy to maintain.
- **Visual** — a small badge/chip anchored near the polygon (source icon + confidence level), plus the highlight-effect modulation from §9.3. Kept as a lightweight React overlay in the same family as `POIMarkerOverlay.tsx`, not a new dialog/panel.

## 10. Reusability for future urban-planning projects

Everything above is deliberately generic — no site name, coordinates, or project-specific value appears in any interface or scoring weight. The two things that make this Al-Safa-specific in the notebook are explicitly **excluded** from the core module and documented as a separate, optional extension pattern:

- **The DXF/CAD override** (notebook Step 8): "trust CAD for precise shape, trust GIS for real-world position/rotation, anchor one to the other." This is a genuinely reusable *pattern* for any project that has an as-built drawing, but it's naturally a follow-on capability — e.g. a future `SiteBoundarySource.SurveyDrawing` + a `IBoundaryShapeOverride` step that runs after the core resolver and re-anchors its shape — not part of the v1 slice.
- **Project-specific config values** (search radius, target area for DXF matching, etc.) stay in per-invocation input/options, never constants in the resolver itself.

Adding a new project later means: pick a location, call the same `SiteBoundaryResolverTool`, get a polygon + confidence + source back, see it rendered with the same highlight effect. No new backend module should be needed for a second park/plaza/campus.

## 11. Explicitly deferred / open questions

- **Multimodal `IAIProvider` widening** — real work, correctly out of scope for v1 (§7.2). Worth its own future spec if other features also want vision.
- ~~**`EffectComposer`/bloom pipeline**~~ — **resolved**: confirmed no post-processing pipeline exists on the GIS render path; v1 uses shader-only additive glow, no `EffectComposer` addition (§9.3).
- **Boundary editing** (draw/adjust a polygon manually) — the notebook itself punted on this (approve/reject/manual-radius only); same scope cut applies here for v1.
- **Persistence/history of approved boundaries** — deferred per §8 until a concrete need shows up.
- Whether this should go through the formal spec-kit process (`speckit-specify` → `plan` → `tasks`) before implementation, matching how `037`/`027` were built — recommended once this write-up is agreed on, given the change touches Domain/Application/Infrastructure/Persistence-adjacent/Frontend layers.

## 12. New files at a glance

```
src/AskLucy.Domain/SiteBoundaries/
  GeoPoint.cs
  SiteBoundaryPolygon.cs
  SiteBoundarySource.cs
  BoundaryConfidenceLevel.cs
  SiteBoundaryResult.cs

src/AskLucy.Application/SiteBoundaries/
  IBoundaryCandidateProvider.cs
  BoundaryCandidate.cs
  BoundaryScoringOptions.cs
  BoundaryCandidateScorer.cs
  IBoundaryResolutionService.cs / BoundaryResolutionService.cs
  BoundaryResolutionOutcome.cs
  GeometryMath.cs

src/AskLucy.Application/Agents/Tools/
  SiteBoundaryResolverTool.cs        (+ one DI registration line)

src/AskLucy.Infrastructure/Boundaries/
  OverpassBoundaryCandidateProvider.cs
  (Phase 2) GeminiBoundaryVisionAnalyzer.cs + IBoundaryVisionAnalyzer.cs

src/AskLucy.Web/ClientApp/src/store/
  activeSiteBoundaryStore.ts

src/AskLucy.Web/ClientApp/src/viewer/layers/gis/
  SiteBoundaryRenderer.ts

src/AskLucy.Web/ClientApp/src/viewer/effects/
  AnimatedBorderHighlight.ts

src/AskLucy.Web/ClientApp/src/features/viewer/components/
  SiteBoundaryOverlay.tsx
```
