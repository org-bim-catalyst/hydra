# API Contract: Map Capture

**Feature**: [spec.md](../spec.md) | **Plan**: [plan.md](../plan.md) | **Date**: 2026-10-19

## Endpoint

```http
GET /api/v1/map-captures
```

Returns a cropped PNG of the requested location. The request is read-only and idempotent; nothing is persisted.

### Authentication

Required header:

```http
X-Api-Key: <service key>
```

Keys are configured under `MapCapture:ApiKeys`. A missing, unknown, or unconfigured key returns `401`.

### Query parameters

| Name | Type | Required | Default | Notes |
|---|---|---|---|---|
| `location` | string | One of `location` or coordinates | — | Place name or address; geocoded server-side. Ignored when coordinates are supplied |
| `latitude` | number | With `longitude` | — | −90…90 |
| `longitude` | number | With `latitude` | — | −180…180 |
| `mode` | string | No | `satellite` | `roadmap`, `satellite`, `hybrid`, `buildings-only` |
| `overlays` | string | No | none | Comma-separated: `building-footprints`. `site-boundary` is rejected as unavailable |
| `viewSizeMeters` | integer | No | `1000` | 100–20,000; width of the captured area |
| `source` | string | No | the configured default | `esri` or `google` — which imagery provider serves the capture |
| `crop` | integer | No | the configured default | 0–200; pixels removed from each of the four edges |
| `zoom` | integer | No | the configured default | 14–21; the framing level. Omitted means the view size decides it |

Coordinates take precedence over `location`.

`source`, `crop` and `zoom` fall back to the defaults an administrator configured on the admin
AI Tools page (`GET /api/v1/admin/ai-tools/settings`), and to the catalogue's own defaults when
nothing was ever configured — Esri, a 32 px crop, zoom-to-fit, and the colour correction on.

**The two providers are not interchangeable.** Esri's export endpoint cannot assemble its tiles
past zoom 18, so a higher level is refused there with `422` rather than answered from Google.
Esri serves satellite only; a non-satellite mode is refused on it with `422`. Nothing is ever
quietly answered by the other provider.

### Success response

```http
HTTP/1.1 200 OK
Content-Type: image/png
Content-Length: <bytes>
X-Map-Capture-Resolved-Location: <name or "lat,lng">
X-Map-Capture-Mode: satellite
X-Map-Capture-Width: 1216
X-Map-Capture-Height: 1216
```

Body: PNG bytes. The default output is 1216×1216 (1280×1280 fetched, 32 px cropped from each
edge — a wider crop also removes the provider's own framing, which is where Google's logo and
attribution line sit).

### Error responses

All errors are RFC 7807 ProblemDetails with a stable `title` and a human-readable `detail`.

| Status | Title | When |
|---|---|---|
| 400 | `Invalid capture request` | Missing both location forms; coordinate only one of the pair; invalid mode; invalid source; view size or crop out of range; zoom out of range; image too small to crop |
| 401 | `Service authentication required` | Missing, unknown, or unconfigured API key |
| 404 | `Location not found` | Geocoding returned zero candidates |
| 422 | `Capture not available` | Requested mode is not supported by the chosen provider (e.g. non-satellite on Esri), requested overlay is unsupported (`site-boundary`), the requested zoom is beyond what the chosen provider can serve, or the encoded image exceeds 10 MB |
| 429 | `Too many requests` | Caller exceeded the configured rate limit |
| 502 | `Upstream provider unavailable` | Geocoding or imagery provider failed or timed out |

Example:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-11.1",
  "title": "Location not found",
  "status": 404,
  "detail": "No geocoding match was found for 'nowhere in particular'.",
  "traceId": "0HN8A2K4Q9V2T:00000003"
}
```

Every ProblemDetails response carries the request's correlation id as `traceId` (constitution §6), matching the global exception middleware's shape.

### Examples

```bash
curl -sS -D headers.txt \
  -H "X-Api-Key: $MAP_CAPTURE_KEY" \
  "https://host/api/v1/map-captures?location=Al%20Safa%20Park%202&mode=satellite" \
  -o capture.png
```

```bash
curl -sS \
  -H "X-Api-Key: $MAP_CAPTURE_KEY" \
  "https://host/api/v1/map-captures?latitude=25.1801&longitude=55.2451&mode=hybrid&overlays=building-footprints&viewSizeMeters=600" \
  -o capture.png
```

### Guarantees

- The body is always a complete PNG or a ProblemDetails JSON document — never an empty or partial image.
- Exactly the requested number of pixels is removed from each edge (32 by default).
- Ephemeral by contract: no file, row, or conversation attachment is created.
- `buildings-only` is an approximation based on Google's `landscape.man_made` styling; it does not guarantee one outline per building. For exact footprints, request `overlays=building-footprints` on `satellite` or `roadmap`.
- Google's output is colour-corrected by default, which restores the contrast and saturation its web tone mapping took. It cannot remove what is baked into the pixels — Google's logo and attribution line, or an aircraft that happened to be passing — and the crop is what removes the provider's own branding.
