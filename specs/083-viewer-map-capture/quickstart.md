# Quickstart: Viewer Map Capture (server-to-server)

**Feature**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Contract**: [contracts/map-capture-api.md](contracts/map-capture-api.md)

## Prerequisites

- .NET 10 SDK.
- A Google Maps API key with **Geocoding API** and **Maps Static API** enabled (the platform already uses this key for geocoding and boundary imagery).
- For local runs, store the key and a service key in user-secrets — never in tracked files:

```powershell
dotnet user-secrets set "Geocoding:GoogleMapsApiKey" "<key>" --project src/AskLucy.Web
dotnet user-secrets set "MapCapture:ApiKeys:0:Name" "local-pipeline" --project src/AskLucy.Web
dotnet user-secrets set "MapCapture:ApiKeys:0:Key" "<long-random-service-key>" --project src/AskLucy.Web
```

Optional tuning (defaults shown):

```json
"MapCapture": {
  "DefaultViewSizeMetres": 1000,
  "MinViewSizeMetres": 100,
  "MaxViewSizeMetres": 20000,
  "MaxImageBytes": 10485760,
  "RateLimitPerMinute": 60
}
```

The configuration keys are spelled in British English (`Metres`); only the query parameter is
`viewSizeMeters`.

No database migration is required.

## Run

```powershell
dotnet run --project src/AskLucy.Web
```

Default local URL: `http://localhost:7170` (check `Properties/launchSettings.json`).

## Validate end-to-end

1. **Place-name capture (defaults)**

```bash
curl -sS -D headers.txt \
  -H "X-Api-Key: $MAP_CAPTURE_KEY" \
  "http://localhost:7170/api/v1/map-captures?location=Al%20Safa%20Park%202" \
  -o capture.png
```

Expected: `200`, `Content-Type: image/png`, `X-Map-Capture-Mode: satellite`, `X-Map-Capture-Width: 1252`, `X-Map-Capture-Height: 1252`.

2. **Dimensions and crop**

```bash
magick identify capture.png
```

Expected: `capture.png PNG 1252x1252 ...` — exactly 28 px smaller than the 1280 px provider image.

3. **Coordinates and a named mode**

```bash
curl -sS -H "X-Api-Key: $MAP_CAPTURE_KEY" \
  "http://localhost:7170/api/v1/map-captures?latitude=25.1801&longitude=55.2451&mode=hybrid&viewSizeMeters=600" \
  -o hybrid.png
```

Expected: `200`; image is a 1252×1252 hybrid render centred on those coordinates.

4. **Building footprints overlay**

```bash
curl -sS -H "X-Api-Key: $MAP_CAPTURE_KEY" \
  "http://localhost:7170/api/v1/map-captures?location=Burjuman&mode=satellite&overlays=building-footprints" \
  -o footprints.png
```

Expected: `200`; visible stroked outlines over the imagery. If the provider returns no footprints for the area, the capture still succeeds without outlines — "no buildings here" is a valid result, not an error.

5. **Authentication**

```bash
curl -sS -o - -w "\n%{http_code}\n" "http://localhost:7170/api/v1/map-captures?location=Dubai"
```

Expected: `401` ProblemDetails, `title: Service authentication required`.

6. **Validation and failures**

| Request | Expected |
|---|---|
| No `location`, no coordinates | `400 Invalid capture request` |
| `latitude` without `longitude` | `400` |
| `mode=terrain` | `400` |
| `location=zzzz-not-a-place` | `404 Location not found` |
| `overlays=site-boundary` | `422 Capture not available` |
| `viewSizeMeters=50` | `400` |

7. **No persistence**

After any successful call, confirm no new file under the configured file-storage root and no new database rows. The endpoint writes nothing.

## Automated tests

```powershell
dotnet test tests/AskLucy.Application.Tests --filter "FullyQualifiedName~MapCaptures"
dotnet test tests/AskLucy.Infrastructure.Tests --filter "FullyQualifiedName~MapCaptures"
dotnet test tests/AskLucy.Web.Tests --filter "FullyQualifiedName~MapCaptures"
```

- Application: coordinates beat place names; geocoding failure and zero-match mapping; default mode/overlays; view-size bounds.
- Infrastructure: mode→`maptype` mapping; `buildings-only` style rules; 14 px crop on a synthetic image; >10 MB rejection; overlay pixel mapping.
- Web: missing/wrong/valid key; content type; ProblemDetails statuses; rate-limit partition per caller.

**Web.Tests note**: `PERSISTENCE_TESTS_CONNECTION_STRING` must be set (value in `appsettings.Development.json`) or virtually every test fails at host startup.

## Definition of done

- `dotnet build` and the three filtered test runs pass.
- `dotnet format` CI gate passes (no `\r\r\n` line endings).
- No new NuGet package, database entity, or migration.
- Logs show the caller name, resolved location, mode, overlays, and elapsed time on every request; they never show the API key.
