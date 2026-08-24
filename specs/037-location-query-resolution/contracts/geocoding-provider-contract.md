# Contract: Geocoding Provider (Nominatim Forward Search)

**Spec**: 037-location-query-resolution (research.md Decisions 4/5) | **Date**: 2026-08-23

## Purpose

`IGeocodingProvider.SearchAsync(query, cancellationToken)` resolves a free-text place
name/query to zero or more candidate real-world locations, each carrying enough
information for `LocationResolutionService` to run the confidence algorithm (research.md
Decision 5) and decide `Confirmed` / `Ambiguous` / `NotFound`.

## Upstream Request

```
GET https://nominatim.openstreetmap.org/search?q={query}&format=json&addressdetails=1&limit=5
User-Agent: AskLucy/1.0 (+https://hydra.bimcatalyst.com)
```

- `{query}` is the place-name text extracted by the intent-classification call
  (contracts/location-intent-classification-contract.md), URL-encoded, sent verbatim —
  no query rewriting/normalization beyond what `Uri`-building already does.
- `limit=5` bounds the candidate set — enough to detect ambiguity (2+ comparable results)
  without over-fetching; the confidence algorithm only ever needs the top two.
- The `User-Agent` header is required by Nominatim's usage policy and is the same literal
  string `WeatherProvider.ResolveLocationNameAsync` already sends for reverse geocoding.
- Uses the existing `"Weather"` named `HttpClient` (`IHttpClientFactory.CreateClient("Weather")`),
  already configured with a 15 s timeout — the same numeric ceiling as spec.md FR-013.

## Upstream Response (per result)

```json
[
  {
    "display_name": "Al Safa 2 Park, Al Safa 2, Dubai, United Arab Emirates",
    "lat": "25.1866",
    "lon": "55.2508",
    "importance": 0.42,
    "type": "park",
    "class": "leisure"
  }
]
```

Only `display_name`, `lat`, `lon`, `importance` are consumed. `lat`/`lon` arrive as
strings (Nominatim convention) and are parsed to `double`; a result that fails to parse is
dropped, not surfaced as a candidate (treated as noise, consistent with the
`MinimumImportanceFloor` filter).

## Mapped Result

Each surviving result becomes one `GeocodingCandidate(LocationName, Latitude, Longitude,
Importance)` (data-model.md). No candidate is dropped except for a parse failure or an
`importance` below `MinimumImportanceFloor` (0.1) — that filtering happens in
`LocationResolutionService`, not in the provider, so `IGeocodingProvider` stays a thin,
faithful mapping of "what Nominatim returned," independently testable from the confidence
algorithm (research.md Decision 5's own contract, not duplicated here).

## Error Handling

- Non-success HTTP status, timeout, or malformed JSON → `NominatimGeocodingProvider`
  throws `GeocodingProviderUnavailableException` (mirrors
  `WeatherProviderUnavailableException`'s existing shape/catch pattern in
  `WeatherProvider`), logged at Warning via `ILogger<NominatimGeocodingProvider>`
  (constitution §4 structured logging — `{Query}`, exception).
- `LocationResolutionService` catches this exception and maps it to
  `LocationResolutionOutcomeType.Unavailable` (data-model.md) — it never propagates into
  the chat stream (constitution §2.VIII).
- An empty result array (`[]`) is a normal, successful response — mapped to `NotFound`,
  not an error.

## Out of Scope

- Nominatim's own rate-limit/caching behavior — spec 035 FR-017's cache is not
  implemented by this feature (spec.md Assumptions: this feature covers only the
  single-confident-match backend path; 037 does not claim to deliver 035's caching layer).
  A future implementation of 035 can wrap `IGeocodingProvider` with a caching decorator
  without changing this contract.
- Reverse geocoding (coordinates → name) — already implemented by `WeatherProvider` for
  an unrelated purpose; not touched by this feature.
