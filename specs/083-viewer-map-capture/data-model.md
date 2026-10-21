# Data Model: Viewer Map Capture (server-to-server)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Date**: 2026-10-19

This feature is stateless. There are no database entities, no tables, and no migrations. The "model" is the request/result shape that crosses the API and Application boundaries.

## Map Capture Request (wire → query)

| Field | Type | Required | Rules |
|---|---|---|---|
| `location` | string | One of `location` or coordinate pair | 1–300 characters; free-text place name/address; geocoded when coordinates are absent |
| `latitude` | double | Together with `longitude` | −90…90; both must be present together |
| `longitude` | double | Together with `latitude` | −180…180; both must be present together |
| `mode` | enum string | No | `roadmap`, `satellite`, `hybrid`, `buildings-only`; default `satellite` |
| `overlays` | comma-separated enum list | No | Subset of `building-footprints`; default empty. `site-boundary` is recognised but rejected as unavailable on this path |
| `viewSizeMeters` | int | No | 100–20,000; default 1,000 (the view's full width) |

Precedence: when both coordinates and `location` are supplied, coordinates are used and geocoding is skipped.

The wire parameter stays `viewSizeMeters` (external contract); internal C# members spell it `Metres`.

## Enums

### MapCaptureMode

| Value | Meaning | Provider mapping |
|---|---|---|
| `Satellite` | Imagery without labels (default) | Google `maptype=satellite`; Esri World Imagery fallback |
| `Hybrid` | Imagery with labels | Google `maptype=hybrid` |
| `Roadmap` | Road map | Google `maptype=roadmap` |
| `BuildingsOnly` | Approximate buildings-only styling | Google `maptype=roadmap` + `landscape.man_made` style rules |

### MapCaptureOverlay

| Value | Meaning |
|---|---|
| `BuildingFootprints` | Stroked outlines of buildings around the resolved location, from `IBuildingFootprintProvider` |
| `SiteBoundary` | Recognised by the parser but **not supported** on this path; the handler rejects it with `OverlayUnavailable` and names it in the detail (FR-008) |

## Application Types

### MapCaptureQuery

```text
MapCaptureQuery(
    string? Location,
    double? Latitude,
    double? Longitude,
    MapCaptureMode Mode = MapCaptureMode.Satellite,
    IReadOnlyList<MapCaptureOverlay>? Overlays = null,
    int? ViewSizeMetres = null)   // null → MapCaptureOptions.DefaultViewSizeMetres
```

### MapImageRequest

Produced by the handler after location resolution:

```text
MapImageRequest(
    double Latitude,
    double Longitude,
    string LocationName,      // resolved name, or the supplied location string
    MapCaptureMode Mode,
    IReadOnlyList<MapCaptureOverlay> Overlays,
    int ViewSizeMetres,       // resolved default already applied by the handler
    string CallerName)        // for logging
```

### MapImageResult

```text
MapImageResult.Success(byte[] Png, int Width, int Height)
MapImageResult.Failure(MapCaptureFailureReason Reason, string Detail)
```

### MapCaptureFailureReason

| Value | Meaning | HTTP |
|---|---|---|
| `LocationNotFound` | Geocoding returned zero candidates | 404 |
| `GeocodingUnavailable` | Geocoding provider failed or timed out | 502 |
| `ModeUnavailable` | Requested mode cannot be produced by the configured provider | 422 |
| `OverlayUnavailable` | Requested overlay cannot be produced for this location/path | 422 |
| `ProviderUnavailable` | Imagery provider failed, timed out, or returned a non-image | 502 |
| `CropTooSmall` | Image is ≤ 28 px in either dimension | 400 |
| `ImageTooLarge` | Encoded PNG exceeds 10,485,760 bytes | 422 |

## Validation Rules (FluentValidation)

- Exactly one location form must resolve: coordinates pair valid, or `location` non-blank.
- `latitude` and `longitude` must be supplied together.
- `viewSizeMeters` within `MapCaptureOptions` bounds.
- `mode` must be a defined enum value.
- Each overlay must be a defined enum value; `site-boundary` passes enum validation and is rejected later with `OverlayUnavailable` so the error message can name the unsupported value.

## Persistence

None. No EF Core entity, `DbSet`, configuration, repository, or migration. The endpoint is read-only with respect to platform data (FR-014).
