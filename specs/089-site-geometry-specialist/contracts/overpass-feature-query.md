# Contract: Shared Overpass Feature Query

**Introduced by**: SPEC-089, for the site-geometry specialist; the first extraction of the transport
SPEC-042's boundary search already used. Consumers: `OverpassBoundaryCandidateProvider` (refactored),
`OverpassSiteGeometryDataProvider` (new). Future consumers: the remaining site-analysis specialists.

## Interface

```csharp
namespace AskLucy.Application.Overpass;

public sealed record OverpassTagFilter(string Key, IReadOnlyList<string>? Values, bool IncludeRelations = false);

public sealed record OverpassFeatureQuery(GeoPoint Center, int RadiusMeters, IReadOnlyList<OverpassTagFilter> Filters);

public sealed record OverpassFeature(
    string Id, string ElementType, IReadOnlyDictionary<string, string> Tags, IReadOnlyList<GeoPoint> Geometry);

public interface IOverpassFeatureQueryProvider
{
    Task<IReadOnlyList<OverpassFeature>> QueryAsync(
        OverpassFeatureQuery query, CancellationToken cancellationToken = default);
}
```

Implementation: `Infrastructure/Overpass/OverpassFeatureQueryProvider`, registered scoped in
`Infrastructure/DependencyInjection.cs`.

## Query semantics

| Filter shape | Statements emitted |
|---|---|
| `Values == null`, `IncludeRelations = false` | `way(around:R,lat,lon)["key"];` |
| `Values == null`, `IncludeRelations = true` | the way statement **and** `relation(around:R,lat,lon)["type"="multipolygon"]["key"];` |
| `Values != null` | one way statement per value, plus one relation statement per value when `IncludeRelations` |
| more than one filter | concatenated inside one `( … );` block, in declaration order |

Envelope: `[out:json][timeout:25];(<statements>);out geom;`. The user's text never enters the query;
only the server-resolved centre and radius and the hard-coded filters do.

## Transport semantics (inherited from SPEC-042, unchanged)

- Named `HttpClient` `"Overpass"` (30 s timeout), `POST {baseUrl}interpreter`, form field `data`.
- Endpoint order: `OverpassOptions.SearchBaseUrl`, then each `MirrorBaseUrls` entry.
- Up to **3 attempts**, rotating to the next host each time, 1 s apart.
- `User-Agent: AskLucy/1.0 (+https://hydra.bimcatalyst.com)`.
- `HttpRequestException`, `TaskCanceledException` (non-caller-cancellation) and `JsonException` map to
  `BoundaryProviderUnavailableException`. Caller cancellation is rethrown.
- No caching (unchanged from SPEC-042's "no caching by design").

## Result mapping

| Overpass element | `OverpassFeature` |
|---|---|
| `way` | `Id = "osm_way_{id}"`, `ElementType = "way"`, geometry exactly as returned (open or closed) |
| `relation` | `Id = "osm_relation_{id}"`, `ElementType = "relation"`, geometry = largest assembled outer ring; omitted if no ring closes |
| other types | omitted |

Ring assembly (`OverpassGeometry.AssembleLargestOuterRing`) is the code moved out of
`OverpassBoundaryCandidateProvider`: joins outer member ways end to end, reverses as needed, drops a
chain that cannot close, ignores inner members.

## Compatibility requirement

`OverpassBoundaryCandidateProvider`'s behaviour must not change: same candidate filters, same
relations-only-for-curated-values rule, same retry/mirror behaviour, same exception type, same
candidate records. `OverpassBoundaryCandidateProviderTests` must pass unmodified; it is the regression
guard for this contract.
