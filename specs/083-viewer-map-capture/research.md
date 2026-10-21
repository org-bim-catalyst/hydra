# Research: Viewer Map Capture (server-to-server)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-19

## D1 — Imagery provider and mode mapping

**Decision**: Use Google Static Maps as the primary renderer. Map modes as follows:

| API mode | Google Static Maps request |
|---|---|
| `satellite` (default) | `maptype=satellite`, `format=png` — no labels |
| `hybrid` | `maptype=hybrid`, `format=png` — imagery with labels |
| `roadmap` | `maptype=roadmap`, `format=png` |
| `buildings-only` | `maptype=roadmap` with style rules hiding everything except `landscape.man_made` |

When no Google Maps key is configured, fall back to Esri World Imagery for `satellite` only; every other mode returns an explicit "mode unavailable" failure. The platform already has both providers and the same key used by geocoding and boundary work; no new provider is introduced.

**Rationale**: Google Static Maps is the same basemap the viewer renders on, the existing key is already used by `GoogleSatelliteImageProvider` and `GoogleRenderedFillBoundaryExtractor`, and the `GoogleStaticMaps` HttpClient is already registered with a 30-second timeout. Static Maps returns the exact requested pixel size, which makes the 14 px crop deterministic.

**Alternatives considered**:
- *Headless browser rendering the viewer* — heavy, needs Chromium, no existing infrastructure, and defeats the purpose of a server-only service.
- *Esri World Imagery as primary* — different reference frame from the viewer's Google basemap and no roadmap/hybrid styles; kept only as a satellite fallback.
- *Google Maps Tiles API* — requires stitching multiple tiles and a session token; more code for the same image.

**Buildings-only fidelity**: the viewer's `buildings-only` uses a cloud-styled vector Map ID, which Static Maps cannot reference. The `landscape.man_made` recipe is an approximation: it shows man-made land-use shapes, not strictly building footprints. This limitation must be stated in the API contract. If a caller needs exact building footprints, it should request the `building-footprints` overlay on a base mode instead.

**Wire parsing**: mode and overlay query values are lower-case and hyphenated (`buildings-only`). The controller accepts them as strings and maps them explicitly; default ASP.NET enum binding only accepts the C# member name and would reject the hyphenated form.

## D2 — Service-to-service authentication

**Decision**: Add a `ServiceApiKey` authentication scheme to the existing ASP.NET Core authentication setup. Callers send `X-Api-Key`; keys are configured under `MapCapture:ApiKeys` as `{ Name, Key }` pairs. The handler compares the presented key with each configured key using `CryptographicOperations.FixedTimeEquals` and, on success, builds a principal with the caller's `Name` as `ClaimTypes.NameIdentifier` and a `Service` role. The controller requires this scheme.

If no keys are configured, the endpoint returns `401` with a clear ProblemDetails title; startup logs a warning. The numeric options are validated at startup (`Validate` + `ValidateOnStart`) as constitution §4 requires — every one has a code default, so this cannot fail a healthy host. `ApiKeys` itself is deliberately not validated and may be empty: the documented repo gotcha (`ValidateOnStart` crashing the whole app) applies to required options with no default, not to these.

**Rationale**: The platform has JWT bearer for users and no machine-credential mechanism. Static API keys are the smallest mechanism that satisfies "trusted services", fits the configuration model (`appsettings.Production.json` / user-secrets), and can be rotated by configuration change. Constant-time comparison avoids timing leakage; keys are never logged.

**Alternatives considered**:
- *Client-credentials JWT* — requires a token endpoint, client registry, and issuance flow; disproportionate for one service endpoint.
- *Reusing user JWT* — wrong identity model; a pipeline has no user session.
- *mTLS* — strong, but needs certificate distribution and hosting changes.

## D3 — Location resolution

**Decision**: Accept either `latitude`+`longitude` (both required together) or `location` (free text). Coordinates win when both are supplied. For free text, call `IGeocodingProvider.SearchAsync` and take the candidate with the highest `Importance` (the convention `SiteBoundaryResolverTool` already uses). Zero candidates → `404`-style "location not found" failure. `GeocodingProviderUnavailableException` → `502`-style "geocoding unavailable" failure.

**Rationale**: Reuses the existing provider abstraction, including the Google/Nominatim switch and its 30-second timeout correction. No new geocoding code and no dependency on a viewer session.

**Alternatives considered**: `ILocationResolutionService` — adds confirmation/confidence logic designed for chat and can return multiple candidates; a service caller needs one deterministic point. Not used.

## D4 — Cropping and encoding

**Decision**: Decode the provider image with Magick.NET (`MagickImage`), draw overlays, crop 14 px from each edge (`crop.Width-28 × crop.Height-28` with offset 14,14), encode PNG, and reject when the encoded length exceeds 10 MB (10,485,760 bytes) or when either dimension is ≤ 28 px. Magick.NET-Q8-AnyCPU is already referenced and used by `RasterImage`, `ImageThumbnailGenerator`, and the preview pipeline.

**Rationale**: No new dependency; the library is already the platform's image codec. Drawing before cropping keeps overlay geometry aligned with the source frame and lets the unconditional crop apply to the whole composition.

**Alternatives considered**:
- *Client-side crop* — deferred with frontend capture.
- *ImageSharp* — the repository deliberately moved off it (commercial licensing / unpatched advisories).
- *Cropping via the provider* — Static Maps cannot crop 14 px off each edge; it renders exactly the requested size.

## D5 — Overlays on the server path

**Decision**: Support `building-footprints` only. Use the existing `IBuildingFootprintProvider.SearchAsync(center, radius)`; draw each returned ring as a 2 px stroke in a high-contrast colour with Magick.NET. Any other overlay name, including `site-boundary`, returns an explicit `422` "overlay unavailable" failure. Default is no overlays.

**Rationale**: Building footprints are a cheap provider call that needs only a point and radius. Site-boundary resolution requires `IBoundaryResolutionService.ResolveAsync(confirmedLocation, userChatId)`, which is chat-scoped, AI-assisted, and measured in seconds to tens of seconds; putting it on a 10-second capture path would repeat the production regression where boundary resolution blocked the viewer. A chat-independent boundary lookup is a separate future feature.

**Alternatives considered**:
- *Both overlays* — would make SC-001 unachievable on slow boundary runs and couple capture to Gemini/Overpass availability.
- *Google Static Maps `path=` parameters* — simple for a handful of rings, but URL length limits and the 100-point cap make building footprints unreliable; drawing after fetch works identically for both providers.

## D6 — Framing and output size

**Decision**: Request a square 640×640 scale-1 Static Maps image with `scale=2`, giving 1280×1280 physical pixels; after the 14 px per-edge crop the response is 1252×1252 PNG. The default view is 1000 m across (±500 m from the resolved point). Callers may pass `viewSizeMeters` between 100 and 20,000; the zoom is chosen with the existing `StaticMapFraming.ChooseZoomToFit` math (clamped 14–20), which guarantees the requested extent fits without clipping.

**Rationale**: `StaticMapFraming` already encapsulates the Web Mercator framing used by every Google Static Maps caller and is proven against live imagery. Scale 2 keeps labels and footprints legible. A fixed output size keeps the contract simple and non-square requests out of scope.

**Alternatives considered**: caller-supplied pixel width/height — more contract surface, and provider limits cap the useful range anyway.

## D7 — Persistence

**Decision**: None. The image is returned in the response stream and discarded. No database row, no `IFileStorage` write, no temp file. Nothing is written to disk, including intermediate processing.

**Rationale**: The spec defers all persisted captures (conversation/user association, chat-id+timestamp names, sharing) to a follow-up feature. Keeping this service read-only also satisfies FR-014.

## D8 — Failure taxonomy

**Decision**: Every failure is a typed Application result mapped by the controller to RFC 7807 ProblemDetails:

| Failure | HTTP |
|---|---|
| Missing both location forms, bad coordinates, invalid mode/overlay, view size out of range, image smaller than the crop | 400 |
| No/invalid service API key | 401 |
| Location not found (zero geocoding candidates) | 404 |
| Requested mode or overlay unavailable on this provider/path | 422 |
| Geocoding or imagery provider unavailable/timeout | 502 |
| Rate limit exceeded | 429 |
| Encoded image over 10 MB | 422 |

Unexpected exceptions continue to flow to the existing global exception middleware (ProblemDetails, logged). No path returns an empty 200 or partial image.

**Rationale**: Matches constitution §6 (ProblemDetails) and §VIII (no silent failures). `422` distinguishes "your request is valid but this deployment/path cannot satisfy it" from a malformed request (`400`) and an upstream outage (`502`).

## D9 — Rate limiting and configuration

**Decision**: Add a `map-capture-endpoints` fixed-window policy partitioned by the authenticated caller name (`ClaimTypes.NameIdentifier`), falling back to the client IP. Default 60 requests per minute per caller, configured under `MapCapture:RateLimitPerMinute`. Keys and the limit live under:

```json
"MapCapture": {
  "ApiKeys": [ { "Name": "<caller>", "Key": "<secret>" } ],
  "DefaultViewSizeMetres": 1000,
  "MinViewSizeMeters": 100,
  "MaxViewSizeMeters": 20000,
  "MaxImageBytes": 10485760,
  "RateLimitPerMinute": 60
}
```

Secrets are supplied via user-secrets or `appsettings.Production.json`, never committed.

**Rationale**: Follows the existing rate-limit style in `Program.cs`; partitioning by the service identity avoids making all callers share one bucket (the documented `Identity.Name` null trap is avoided by using `NameIdentifier`).

## D10 — Dependencies and data

**Decision**: No new NuGet package, no database entity, no migration, no frontend change. Reused: `IGeocodingProvider`, `IBuildingFootprintProvider`, `GoogleStaticMaps`/`EsriWorldImagery` HttpClients, `StaticMapFraming`, Magick.NET, MediatR, FluentValidation, ProblemDetails, and the existing options/logging patterns.

## Sources

- Google Maps Static API styling reference (feature types; `landscape.man_made` as the closest "buildings" selector): [Maps Static API styling](https://developers.google.com/maps/documentation/maps-static/styling), [Style and display buildings (Stack Overflow Q54888698)](https://stackoverflow.com/questions/54888698/style-and-display-buildings)
- Existing code: `GoogleSatelliteImageProvider`, `EsriSatelliteImageProvider`, `StaticMapFraming`, `IGeocodingProvider`, `IBuildingFootprintProvider`, `RasterImage`.
