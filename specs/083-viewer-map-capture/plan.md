# Implementation Plan: Viewer Map Capture

**Branch**: `083-viewer-map-capture` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/083-viewer-map-capture/spec.md`

## Summary

Add a server-to-server endpoint that returns a cropped map image for a supplied location. The caller sends a place name (geocoded by the existing `IGeocodingProvider`) or coordinates, plus an optional mode, overlay list, and view size. The service fetches imagery from Google Static Maps (Esri World Imagery as a satellite-only fallback), draws requested overlays, crops 14 px from each edge with the already-installed Magick.NET, enforces the 10 MB cap, and streams PNG bytes back. Nothing is persisted. Callers authenticate with a service API key, a new scheme on top of the existing JWT setup. No database changes, no frontend changes, no new packages.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: ASP.NET Core, MediatR, FluentValidation, Magick.NET-Q8-AnyCPU (already referenced), the existing `IGeocodingProvider`, `IBuildingFootprintProvider`, the `GoogleStaticMaps` and `EsriWorldImagery` HttpClients, and the existing Google Maps API key

**Storage**: None — the response is streamed and discarded; no database, no local files

**Testing**: xUnit (Application handler tests, Infrastructure renderer tests with a stubbed `HttpMessageHandler`, Web integration tests via the existing `CustomWebApplicationFactory`)

**Target Platform**: ASP.NET Core Web API (Windows/Linux server)

**Project Type**: Web service endpoint inside the existing Clean Architecture web application

**Performance Goals**: 95% of valid requests complete within 10 seconds including geocoding (SC-001); a capture with no overlays is one geocoding call plus one imagery fetch plus in-memory processing

**Constraints**: 10 MB cap after cropping; fixed 14 px per-edge crop; no viewer or frontend dependency; no persistence; no new NuGet package

**Scale/Scope**: Service-to-service; rate-limited per caller key (default 60 requests/minute per caller); a handful of downstream services

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design: still passes.*

| Principle | Assessment |
|---|---|
| I. Clean Architecture | Request/result types and orchestration in Application; provider HTTP calls, image processing, and overlay painting in Infrastructure; a thin controller and auth handler in Web. Dependencies point inward. **Pass** |
| II. SOLID | `IMapImageRenderer` abstracts imagery generation; the geocoding and building-footprint interfaces already exist and are reused. **Pass** |
| III. Simplicity / YAGNI | One endpoint, no persistence, no database, no frontend, no new package; Google Static Maps and Magick.NET already exist in the codebase. **Pass** |
| V. Dependency inversion | Application calls `IMapImageRenderer`, `IGeocodingProvider`, and `IBuildingFootprintProvider`; Infrastructure provides the implementations. **Pass** |
| VI. Separation of concerns | Validation in FluentValidation, orchestration in the handler, image work in Infrastructure, HTTP mapping in the controller. **Pass** |
| VII. Convention over configuration | Reuses the existing versioned route, MediatR, ProblemDetails, rate-limit-policy, `IConfiguration`-bound options, and logging conventions. **Pass** |
| VIII. No silent failures | Every failure is a typed result mapped to ProblemDetails; provider and image failures are logged with caller/location context; no empty or partial images. **Pass** |
| Section 6 API standards | `/api/v1/map-captures`, ProblemDetails on every error, no entity types exposed. **Pass** |
| Section 8 Security | Service API key required; constant-time comparison; keys never logged; request parameters validated; rate limited per caller. **Pass** |
| Section 10 Testing | Unit, infrastructure, and Web integration tests listed in the quickstart. **Pass** |
| Section 14 Observability | Structured logs for accepted requests, geocoding results, provider failures, and processing duration, including the caller name but never the key. **Pass** |

No violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/083-viewer-map-capture/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── map-capture-api.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/AskLucy.Application/
└── MapCaptures/
    ├── MapCaptureMode.cs                 # roadmap | satellite | hybrid | buildings-only
    ├── MapCaptureOverlay.cs              # building-footprints (site-boundary parsed, then rejected)
    ├── MapCaptureOptions.cs              # view-size bounds, byte cap, service keys, rate limit
    ├── MapImageRequest.cs                # resolved center, mode, overlays, view size
    ├── MapImageResult.cs                 # PNG bytes or a typed failure
    ├── MapCaptureFailureReason.cs
    ├── IMapImageRenderer.cs
    └── Queries/CaptureMap/
        ├── CaptureMapQuery.cs
        ├── CaptureMapQueryHandler.cs
        └── CaptureMapQueryValidator.cs

src/AskLucy.Infrastructure/
├── MapCaptures/
│   ├── GoogleStaticMapImageRenderer.cs  # URL build, fetch, overlay paint, crop, PNG encode
│   ├── EsriSatelliteImageRenderer.cs    # satellite-only fallback
│   ├── MapImageRendererRouter.cs        # Google primary, Esri fallback, typed failure otherwise
│   └── MapImageComposer.cs              # lat/lng → pixel overlay paint, 14 px crop, PNG encode, size cap
└── DependencyInjection.cs               # renderer registrations

src/AskLucy.Web/
├── Auth/
│   ├── ServiceApiKeyAuthenticationHandler.cs
│   └── ServiceApiKeyAuthenticationOptions.cs
├── Controllers/v1/MapCapturesController.cs
├── Contracts/MapCaptureContracts.cs     # only if a request DTO is needed; query params otherwise
├── Program.cs                           # auth scheme, rate-limit policy, startup configuration warning
└── appsettings.json / appsettings.Production.json.example

tests/
├── AskLucy.Application.Tests/MapCaptures/          # geocoding precedence, failures, defaults
├── AskLucy.Infrastructure.Tests/MapCaptures/       # URL/mode mapping, 14 px crop, 10 MB cap, overlay math
└── AskLucy.Web.Tests/MapCaptures/                  # auth matrix, validation, problem details, content type
```

**Structure Decision**: The existing Clean Architecture layout. A new `MapCaptures` feature folder mirrors `Buildings` and `SiteBoundaries`; Infrastructure keeps all provider-specific and Magick.NET work, and Web keeps the auth scheme and thin endpoint. The frontend is untouched because capture from the viewer is deferred.

## Complexity Tracking

None.
