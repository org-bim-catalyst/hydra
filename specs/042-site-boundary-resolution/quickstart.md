# Quickstart: Validating Site Boundary Resolution

Proves the feature end-to-end (User Stories 1–4 in `spec.md`) once implementation is complete. This is a validation guide, not a spec of the implementation itself — see `data-model.md` and `contracts/` for the actual shapes.

## Prerequisites

- Backend running: `dotnet run --project src/AskLucy.Web`
- Frontend dev server running: `npm run dev` from `src/AskLucy.Web/ClientApp`
- A logged-in user session in the app (any subscription tier — FR-014 means tier doesn't matter here)
- `Boundaries:Overpass` reachable from the dev machine (public API, no key — same network requirement as the existing `Geocoding` section already needs for Nominatim)

## Scenario A — High-confidence boundary (User Story 1 + 2, P1/P2)

1. Start a new chat and ask: *"Show me Al Safa Park 2."*
2. **Expected**: within 10 seconds (SC-001), the map centers on the site and a highlighted, animated polygon boundary appears that visually matches the park's real extent — not a circle, not just a pin.
3. **Expected**: Lucy's reply states a High confidence level and names OpenStreetMap as the source (FR-004/FR-005).
4. Follow up: *"How sure are you about that?"*
5. **Expected**: Lucy answers using the same already-resolved boundary's confidence/source — no re-resolution, no new map flicker (FR-009, acceptance scenario 3 of User Story 2).

## Scenario B — Low-confidence / fallback (User Story 1 acceptance scenario 3, User Story 2 acceptance scenario 2)

1. Ask about an obscure address with no mapped shape, e.g. a residential street address with no tagged parcel/building polygon nearby.
2. **Expected**: an approximate circular area is shown around the point, visually distinct (e.g., dashed, muted, no animated comets per `AnimatedBorderHighlight`'s `low` mode) from a High-confidence result, and Lucy states this is an approximation, not a confirmed boundary (FR-006).

## Scenario C — No candidate found (User Story 3, FR-007)

1. Ask about a location resolvable to a point but with no plausible boundary candidate at all within the search radius (e.g., open desert/water coordinates).
2. **Expected**: Lucy explicitly states no reliable boundary was found and offers a fallback or asks for more detail — never a silent empty response (SC-005).

## Scenario D — Similarly-plausible candidates (User Story 3 acceptance scenario 2, FR-008)

1. Ask about a site where two nearby OSM features could both plausibly match (e.g., a park that overlaps a larger landuse polygon).
2. **Expected**: the top-scored candidate is still shown highlighted, and Lucy's reply names the other similarly-plausible candidate(s) rather than silently picking one with no disclosure.

## Scenario E — Data source unavailable (FR-012)

1. Simulate `Boundaries:Overpass` being unreachable (e.g., block the host locally, or point `Boundaries:Overpass:BaseUrl` at an invalid endpoint in dev config).
2. Ask about any site.
3. **Expected**: Lucy states boundary resolution isn't available right now — no stale, empty, or fabricated result.

## Scenario F — Reused for a different, unrelated site (User Story 4, FR-011)

1. In a fresh conversation, ask about a site with no relation to Al Safa Park 2 — a different park in a different city, or a university campus.
2. **Expected**: identical behavior/quality bar to Scenario A, with no indication anywhere (config, code path, response wording) that the feature was built around one specific reference project.

## Automated coverage (for CI, not manual click-through)

- **Backend unit — resolution logic**: `dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~SiteBoundaries` — exercises `BoundaryCandidateScorer` and `BoundaryResolutionService` against every `BoundaryResolutionOutcomeType` with a faked `IBoundaryCandidateProvider` (Scenarios A/C/D map directly to `Confirmed`/`NoCandidates`/`Confirmed-with-alternatives`).
- **Backend unit — chat pipeline hook**: `dotnet test tests/AskLucy.Application.Tests --filter FullyQualifiedName~SendChatMessage|RecordActiveSiteBoundary` — the trigger logic (resolve only on an actual site change, skip + inject context message when unchanged, per `chat-pipeline-integration.md`) and the persistence command handler, both with faked dependencies.
- **Backend integration**: `dotnet test tests/AskLucy.Infrastructure.Tests --filter FullyQualifiedName~Boundaries` — `OverpassBoundaryCandidateProvider` against recorded/replayed HTTP fixtures, including a simulated-unavailable case (Scenario E); `dotnet test tests/AskLucy.Persistence.Tests --filter FullyQualifiedName~ActiveBoundary` — round-trips `UserChat.ActiveBoundary` (including the polygon JSON conversion) through a real/test SQL Server instance.
- **Frontend unit**: `npm test -- activeSiteBoundaryStore AnimatedBorderHighlight SiteBoundaryOverlay aiApi` (from `src/AskLucy.Web/ClientApp`) — store replace/clear semantics, confidence-level-driven glow behavior, overlay mount/unmount against a faked `GoogleMapsGisLayerHandle`, and the `__SITE_BOUNDARY__` stream-parsing branch.
- **End-to-end**: extend the existing Playwright map/location suite with Scenario A and Scenario C as the two highest-value automated checks (visible boundary + visible "not found" message).
