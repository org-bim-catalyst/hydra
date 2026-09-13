# Contract: Building Footprints Endpoint

`GET /api/v1/site-buildings`

Retrieves building footprints around a point, through the platform (spec Clarification 3, research
D4). Authenticated like every other endpoint (§6 — `[Authorize]` by default) and **rate-limited**
under a `buildings-endpoints` policy, mirroring `WeatherController`'s `weather-endpoints` — §6
requires every public endpoint carry one, and this one fronts a shared free service that has
already returned 429 to this system.

## Request

| Parameter | Type | Required | Rules |
|---|---|---|---|
| `latitude` | `double` | yes | −90…90 |
| `longitude` | `double` | yes | −180…180 |
| `radiusMetres` | `int` | no | 50…1000, default **200** (the reference implementation's proven working value) |

Validated by FluentValidation (`GetSiteBuildingsQueryValidator`); a violation returns RFC 7807
Problem Details, not an ad-hoc error shape (§6).

## Response `200 OK`

```json
{
  "buildings": [
    {
      "id": "osm_way_123456",
      "ring": [ { "latitude": 25.197, "longitude": 55.274 }, "…closed ring, >= 4 points" ],
      "heightMetres": 42.0,
      "heightProvenance": "known",
      "name": "Example Tower",
      "isSiteBuilding": false
    }
  ],
  "limited": false,
  "excludedCount": 2,
  "radiusMetres": 300
}
```

### Guarantees

- **`ring` is a closed ring of at least 4 points.** Footprints that are not are excluded server-side
  and counted in `excludedCount` (FR-013) — the client never receives geometry it must defend
  against.
- **`heightProvenance` is always present**, resolved by the stated rule below. The client never
  parses an OSM tag (research D5).
- **At most one building has `isSiteBuilding: true`** (research D8).
- **`limited: true` means the result was truncated** by the count cap and the user must be told
  (FR-015).
- **An empty `buildings` array is a success, not an error** — "no buildings here" is a real answer
  and the sun path must still work (FR-014).

### Height resolution (stated, per FR-044)

| Order | Source | `heightProvenance` |
|---|---|---|
| 1 | `height` tag, metres | `known` |
| 2 | `building:levels` × **3.0 m** | `assumed` |
| 3 | Default **9.0 m** | `assumed` |

The 9 m default is three levels at the same 3 m rule used one row above, so the two constants agree
with each other rather than being independently chosen.

### Site-building rule (stated, per FR-012)

The footprint containing the site point; failing that, the footprint whose edge is nearest **within
25 m**; failing that, none.

## Failure

| Condition | Status | Body |
|---|---|---|
| Invalid parameters | `400` | Problem Details, with the failing field |
| Overpass unavailable after retries | `503` | Problem Details, `detail` = "Building data is temporarily unavailable." |
| Unauthenticated | `401` | Standard |
| Rate limit exceeded | `429` | Standard, from the global rate limiter |

`503` is produced from `BuildingProviderUnavailableException`, mirroring
`BoundaryProviderUnavailableException`'s existing treatment. The client renders it as the
`partial` state — sun path continues, buildings notice shown (FR-014, FR-045). It is never
swallowed and never console-only (§2.VIII).

## Provider contract (`Application`)

```csharp
public interface IBuildingFootprintProvider
{
    Task<BuildingFootprintResult> SearchAsync(
        GeoPoint center, int radiusMetres, CancellationToken cancellationToken = default);
}
```

Mirrors `IBoundaryCandidateProvider` deliberately (research D4). The only v1 implementation is
Overpass; an authoritative/cadastral source is an additive `Infrastructure` implementation with a
DI registration change and no `Application` edit (§3 Infrastructure isolation).

### Caching

Results are cached in the already-registered `IMemoryCache`, keyed by rounded latitude/longitude
plus radius, with a stated TTL.

This is **required, not an optimisation**. The spec's own Clarification justifies routing building
data through the platform partly on caching, so leaving it out would leave a stated rationale
unbacked. Practically, building footprints are near-static while Overpass is the least reliable
dependency in the system, and without a cache every open/close of the analysis re-queries it.
Staleness is acceptable and documented, which is exactly the condition §15 sets for caching.

### Reused operational behaviour — not re-derived

The implementation uses the **existing `"Overpass"` named `HttpClient`** and therefore inherits:
3 attempts, rotation across the cluster's own nodes rather than retrying a saturated load balancer,
a **30 s** client timeout (not 15 s — the recorded false-unavailability trap on this host), and the
`[timeout:25]` server-side budget. Re-deriving any of these would discard hard-won operational
knowledge from specs/042's post-release rounds.
