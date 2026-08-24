# Research: POI Viewer Zoom & Focus (SPEC-038)

## Decision 1 — Bounding Box Source

**Decision**: Extend `GoogleMapsGeocodingProvider` to parse the `viewport` object already present in every Google Maps Geocoding API response. The `viewport` contains `northeast` and `southwest` `{lat, lng}` pairs that define the recommended display extent for the place.

**Rationale**: The field is already returned by the API — no additional HTTP call, no cost increase. The existing `GeoGeometry` private record inside `GoogleMapsGeocodingProvider` already captures the geometry object; adding `viewport` is a one-field extension. `NominatimGeocodingProvider` does not return bounding-box data in this format so `ViewportBounds` is nullable on `GeocodingCandidate`.

**Alternatives considered**: Pre-computing a zoom level server-side (rejected — requires guessing viewport pixel dimensions); calling the Places API for richer bounding data (rejected — unnecessary extra API surface and cost).

---

## Decision 2 — ViewportBounds Flow

**Decision**: Add `ViewportBounds?` to `GeocodingCandidate` → `ConfirmedLocationData` → `__LOCATION__` SSE JSON → `activeLocationStore` → `ViewerSurface`. The viewport travels as-is from the geocoding response all the way to the viewer, which fits the bounding box to its own pixel dimensions.

**Rationale**: Clean single-responsibility data flow. Each layer passes the data through without transforming it. The viewer is the only component that knows its own pixel dimensions, so it is the correct place to compute the final camera altitude.

**`ConfirmedLocationData` additions**:
```
ViewportBounds? Viewport       // northeast + southwest lat/lng
string? LocationType           // ROOFTOP | RANGE_INTERPOLATED | GEOMETRIC_CENTER | APPROXIMATE
```

`LocationType` is carried for the fallback case (Decision 3).

---

## Decision 3 — No-Bounding-Box Fallback

**Decision**: When `Viewport` is null, derive camera altitude from `LocationType` using fixed reference altitudes:

| LocationType | Altitude |
|---|---|
| ROOFTOP | 200 m |
| RANGE_INTERPOLATED | 200 m |
| GEOMETRIC_CENTER | 800 m |
| APPROXIMATE | 8 000 m |
| null / unknown | 2 000 m |

**Rationale**: `LocationType` already flows through `GoogleMapsGeocodingProvider` (used for the importance proxy). Adding it to `GeocodingCandidate` and `ConfirmedLocationData` costs nothing and eliminates the need for a second AI classification call to guess place size.

---

## Decision 4 — Display Name: Use Query, Not Formatted Address

**Decision**: `ConfirmedLocationData.LocationName` is set to the user's **extracted query string** (e.g., `"Dubai Mall"`), not the geocoding provider's `formatted_address` (e.g., `"Burj Khalifa - Downtown Dubai - Dubai - UAE"`). The formatted address is stored separately as `GeocodingAddress` on `GeocodingCandidate` for logging/debugging but is never used as the display label.

**Rationale**: The `formatted_address` describes the geographic area surrounding the geocode point, not the specific POI the user named. The user's query is always the most accurate description of what they asked for. In `LocationResolutionService`, the extracted `query` string is already available — using it as `LocationName` is a one-line change.

**Change in `LocationResolutionService`**:
```csharp
// Before
LocationName = candidate.LocationName   // formatted_address from geocoding
// After
LocationName = query,                   // user's extracted query
GeocodingAddress = candidate.LocationName  // kept for diagnostics
```

---

## Decision 5 — Zoom Command Detection

**Decision**: Zoom commands ("zoom in", "get closer", "pull back", etc.) are detected by a new lightweight `ViewerZoomDetector` class that pattern-matches the user's message against a keyword table — no AI call needed. It runs concurrently in `SendChatMessageCommandHandler` alongside the existing location resolution task.

**Rationale**: Zoom intent is unambiguous and does not require semantic classification. A keyword table (configurable, not hardcoded) is fast, zero-cost, deterministic, and trivially testable. An AI classification call would add latency and token cost for no quality gain.

**New SSE event**: `__ZOOM__{direction}` where `direction` is `in` or `out`. Parsed by the frontend the same way as `__LOCATION__`.

**Zoom keyword table (initial)**:

| Direction | Keywords |
|---|---|
| in | zoom in, get closer, fly closer, closer, focus, zoomed in, come in, move in |
| out | zoom out, pull back, fly back, wider, back up, more context, move out, zoom back |

---

## Decision 6 — POI Marker: WebGL Overlay View

**Decision**: The POI marker is rendered using the Google Maps `WebglOverlayView` API. Each marker style is a separate Three.js scene graph attached to the same `WebGLRenderingContext` the map uses, positioned via `coordinateTransformer.fromLatLngAltitude()` in the overlay's `onDraw` callback.

**Rationale**: This is the only mechanism that anchors custom WebGL geometry to geographic coordinates on a Google Maps 3D vector map. The user explicitly identified this API. It avoids DOM-positioned overlays (which don't track tilt/rotation correctly) and integrates with the existing Three.js usage in the viewer engine.

**Marker styles and geometry**:

| Style | Three.js Geometry | Default |
|---|---|---|
| Pulsing ring | `TorusGeometry` + animated uniform scale + alpha pulse | ✓ |
| Classic pin | `CylinderGeometry` (stem) + `SphereGeometry` (head) | |
| 3D extruded highlight | `CylinderGeometry` (transparent column) | |
| Simple dot | `SphereGeometry` (small) | |

All styles share a floating text label rendered as an HTML overlay positioned via the marker's projected screen coordinates.

---

## Decision 7 — Marker Style Persistence

**Decision**: `MarkerStyle` is stored in `localStorage` under the key `viewer.markerStyle`. It is read on `POIMarkerOverlay` mount and written whenever the user changes the selection in the control panel. All `localStorage` access is wrapped in try/catch per constitution §VIII (no silent failures). The default when absent or invalid is `'pulsing-ring'`.

**Rationale**: Marker style is a per-user, per-browser preference — not data that needs to round-trip through the API or persist across devices. `localStorage` is the correct scope. A Zustand store (`useMarkerStyleStore`) wraps the persistence so components remain unaware of the storage mechanism.

---

## Decision 8 — `viewerEngine` API Extension

**Decision**: Add two new methods to the viewer engine alongside the existing `zoomToLocation(lat, lng, zoom)`:

```ts
fitBounds(ne: {lat: number, lng: number}, sw: {lat: number, lng: number}): void
zoomBy(direction: 'in' | 'out'): void
```

`fitBounds` computes camera altitude from the bounding box diagonal and the current viewport aspect ratio, then calls the Google Maps camera API.  
`zoomBy` increments/decrements the current camera altitude by a fixed factor (÷2 for in, ×2 for out), clamped to the viewer's min/max altitude.

**Rationale**: These two methods satisfy all FR-001, FR-002, FR-003, FR-006, and FR-009 without exposing raw camera internals to `ViewerSurface` or the marker layer. The factor-of-2 step matches standard map doubling convention and gives a satisfying, predictable zoom feel.

---

## Decision 9 — `ChatStreamChunk` Extension

**Decision**: Add `ViewerZoomCommand? ViewerZoom` alongside the existing `ConfirmedLocation?` on `ChatStreamChunk`. `ViewerZoomCommand` carries a single field: `Direction` (`"in"` | `"out"`). It is a final-chunk-only payload, consistent with the existing pattern.

```csharp
sealed record ViewerZoomCommand(string Direction);  // "in" | "out"
```

The `AiController` emits `__ZOOM__{direction}` when `ViewerZoom != null`, in the same trailing-event block as `__LOCATION__`.

---

## Decision 10 — Constitution Compliance

All changes follow Clean Architecture:
- `ViewportBounds` record defined in `Application.Locations` (Domain-level value object, no external dependencies).
- `ViewerZoomDetector` in `Application.Locations` — pure string logic, zero infrastructure dependencies, fully unit-testable.
- `GoogleMapsGeocodingProvider` (Infrastructure) extended to parse `viewport` — no Application layer change to the interface.
- Frontend viewer engine extensions (`fitBounds`, `zoomBy`) are additive — no existing callers broken.
- `useMarkerStyleStore` wraps `localStorage` — error-path handled (try/catch, fallback to default).
