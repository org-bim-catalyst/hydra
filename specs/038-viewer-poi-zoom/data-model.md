# Data Model: POI Viewer Zoom & Focus (SPEC-038)

## New Records

### `ViewportBounds` (Application.Locations)

Value object. Nullable on all parent records — absent when provider returns no bounding box.

```csharp
sealed record ViewportBounds(
    double NortheastLat,
    double NortheastLng,
    double SouthwestLat,
    double SouthwestLng);
```

Validation rules:
- `NortheastLat` and `SouthwestLat` in [-90, 90]
- `NortheastLng` and `SouthwestLng` in [-180, 180]
- `NortheastLat >= SouthwestLat` (north is above south)

---

### `ViewerZoomCommand` (Application.Locations)

Final-chunk-only payload emitted when a zoom intent is detected in the user's message.

```csharp
sealed record ViewerZoomCommand(string Direction);
// Direction: "in" | "out"
```

---

## Modified Records

### `GeocodingCandidate` (Application.Locations — IGeocodingProvider.cs)

**Before**:
```csharp
sealed record GeocodingCandidate(
    string LocationName,
    double Latitude,
    double Longitude,
    double Importance);
```

**After**:
```csharp
sealed record GeocodingCandidate(
    string LocationName,
    double Latitude,
    double Longitude,
    double Importance,
    string? LocationType = null,
    ViewportBounds? Viewport = null);
```

`LocationName` on `GeocodingCandidate` continues to hold the provider's `formatted_address`. The display name shown to the user is set separately in `LocationResolutionService` using the user's query string.

---

### `ConfirmedLocationData` (Application.Ai.Commands.SendChatMessage — ChatStreamChunk.cs)

**Before**:
```csharp
sealed record ConfirmedLocationData(
    double Latitude,
    double Longitude,
    string LocationName,
    double Confidence,
    string Source = "agent");
```

**After**:
```csharp
sealed record ConfirmedLocationData(
    double Latitude,
    double Longitude,
    string LocationName,
    double Confidence,
    string Source = "agent",
    string? LocationType = null,
    ViewportBounds? Viewport = null);
```

`LocationName` is now always the user's query string (or canonical POI name), never the geocoding `formatted_address`. The `LocationType` field carries the Google Maps `location_type` string for frontend fallback altitude logic.

---

### `ChatStreamChunk` (Application.Ai.Commands.SendChatMessage)

**Before**:
```csharp
sealed record ChatStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null,
    ConfirmedLocationData? ConfirmedLocation = null);
```

**After**:
```csharp
sealed record ChatStreamChunk(
    string? ContentDelta,
    ChatUsage? Usage,
    RagRetrievalOutcome? RetrievalOutcome = null,
    MemoryRetrievalOutcome? MemoryOutcome = null,
    ConfirmedLocationData? ConfirmedLocation = null,
    ViewerZoomCommand? ViewerZoom = null);
```

`ViewerZoom` and `ConfirmedLocation` are mutually exclusive in practice but both nullable to keep the record open for extension.

---

## New Classes

### `ViewerZoomDetector` (Application.Locations)

Pure static/injectable keyword matcher. No external dependencies. Takes the user's message text, returns `ViewerZoomCommand?`.

```csharp
sealed class ViewerZoomDetector
{
    public ViewerZoomCommand? Detect(string message);
}
```

Keyword sets are `readonly string[]` fields. Detection is case-insensitive substring search. Returns `null` when no keyword matches.

Zoom-in keywords: `"zoom in"`, `"get closer"`, `"fly closer"`, `"focus on"`, `"closer"`, `"zoomed in"`, `"come in"`, `"move in"`
Zoom-out keywords: `"zoom out"`, `"pull back"`, `"fly back"`, `"wider"`, `"back up"`, `"more context"`, `"move out"`, `"zoom back"`

---

## Frontend State Extensions

### `activeLocationStore` (Zustand)

**Before** state:
```ts
source: 'agent' | 'geolocation' | null
latitude: number | null
longitude: number | null
locationName: string | null
confidence: number | null
```

**After** state (additive — no breaking changes):
```ts
source: 'agent' | 'geolocation' | null
latitude: number | null
longitude: number | null
locationName: string | null
confidence: number | null
locationType: string | null           // NEW: "ROOFTOP" | "RANGE_INTERPOLATED" | "GEOMETRIC_CENTER" | "APPROXIMATE" | null
viewport: ViewportBounds | null       // NEW: northeast + southwest lat/lng
```

`setFromAgent` updated to accept the new fields (optional, backward-compatible).

---

### `useMarkerStyleStore` (new Zustand store)

```ts
type MarkerStyle = 'pulsing-ring' | 'classic-pin' | '3d-highlight' | 'simple-dot'

interface MarkerStyleState {
  markerStyle: MarkerStyle
  setMarkerStyle: (style: MarkerStyle) => void
}
```

Persistence: reads/writes `localStorage` key `viewer.markerStyle`. All access wrapped in try/catch with fallback to `'pulsing-ring'`.

---

### Frontend `ViewportBounds` type

```ts
interface ViewportBounds {
  northeastLat: number
  northeastLng: number
  southwestLat: number
  southwestLng: number
}
```

Used in `activeLocationStore`, `ConfirmedLocationData` SSE payload, and `ViewerSurface`.

---

## Viewer Engine Extensions

### New methods on viewer engine

```ts
fitBounds(ne: { lat: number; lng: number }, sw: { lat: number; lng: number }): void
zoomBy(direction: 'in' | 'out'): void
```

`fitBounds`: computes camera altitude so the bounding box fits within 80% of the viewport's smaller dimension. Uses `google.maps.LatLngBounds` to get the angular span, then derives altitude from the span and the map container's pixel height via a reference formula. Calls `map.moveCamera({ center, tilt, heading, altitude })` or `map.fitBounds()` depending on the Maps API version available.

`zoomBy`: multiplies the current camera altitude by 0.5 (zoom in) or 2.0 (zoom out), clamped to `[50, 500_000]` metres. Calls `map.moveCamera` with `animate: true`.

---

## Fallback Altitude Table (frontend, ViewerSurface)

Used when `viewport` is null:

```ts
const LOCATION_TYPE_ALTITUDE: Record<string, number> = {
  ROOFTOP: 200,
  RANGE_INTERPOLATED: 200,
  GEOMETRIC_CENTER: 800,
  APPROXIMATE: 8000,
}
const DEFAULT_ALTITUDE = 2000
```

---

## SSE Event Format Changes

### `__LOCATION__` (existing, extended)

Serialized `ConfirmedLocationData` as JSON. New fields (`viewport`, `locationType`) appear in the JSON automatically via `System.Text.Json`. Frontend deserialization updated to extract new fields.

Example:
```json
{
  "latitude": 25.1972,
  "longitude": 55.2796,
  "locationName": "Dubai Mall",
  "confidence": 0.90,
  "source": "agent",
  "locationType": "ROOFTOP",
  "viewport": {
    "northeastLat": 25.1985,
    "northeastLng": 55.2812,
    "southwestLat": 25.1958,
    "southwestLng": 55.2779
  }
}
```

### `__ZOOM__` (new)

Format: `__ZOOM__{direction}` where `direction` is `in` or `out`.

Examples:
- `__ZOOM__in`
- `__ZOOM__out`

Emitted by `AiController` in the same trailing-event block as `__LOCATION__`, when `ChatStreamChunk.ViewerZoom != null`.
