# SSE Event Contracts: POI Viewer Zoom & Focus (SPEC-038)

## Overview

The backend emits custom sentinel events at the end of the AI stream. The frontend parser in `aiApi.ts` detects these lines in the SSE `data:` payload and routes them to the appropriate store or action.

---

## Existing Event: `__LOCATION__` (extended)

### Format

```
__LOCATION__{json}
```

Where `{json}` is a compact JSON object (no embedded newlines).

### Schema (after SPEC-038)

```json
{
  "latitude": number,
  "longitude": number,
  "locationName": string,
  "confidence": number,
  "source": "agent",
  "locationType": "ROOFTOP" | "RANGE_INTERPOLATED" | "GEOMETRIC_CENTER" | "APPROXIMATE" | null,
  "viewport": {
    "northeastLat": number,
    "northeastLng": number,
    "southwestLat": number,
    "southwestLng": number
  } | null
}
```

### Field Notes

| Field | Required | Description |
|---|---|---|
| `latitude` | Yes | WGS-84 latitude of the resolved point |
| `longitude` | Yes | WGS-84 longitude of the resolved point |
| `locationName` | Yes | User's original query string (e.g. "Dubai Mall") — not the geocoding formatted address |
| `confidence` | Yes | 0.0–1.0 confidence from `GeocodingCandidate.Importance` |
| `source` | Yes | Always `"agent"` for this flow |
| `locationType` | No | Google Maps `location_type` string; null when provider does not return it |
| `viewport` | No | Bounding box from geocoding response; null when provider does not return it |
| `viewport.northeastLat` | When viewport present | NE corner latitude |
| `viewport.northeastLng` | When viewport present | NE corner longitude |
| `viewport.southwestLat` | When viewport present | SW corner latitude |
| `viewport.southwestLng` | When viewport present | SW corner longitude |

### Frontend Handling

Parsed in `aiApi.ts`. Calls `activeLocationStore.setFromAgent(data)`. `ViewerSurface` reacts to store change:
- When `viewport` is non-null: call `viewerEngine.fitBounds(ne, sw)`
- When `viewport` is null and `locationType` is non-null: call `viewerEngine.zoomToAltitude(altitude)` using the fallback table
- When both null: call `viewerEngine.zoomToLocation(lat, lng, 15)` (legacy behaviour, unchanged)

---

## New Event: `__ZOOM__`

### Format

```
__ZOOM__{direction}
```

Where `{direction}` is one of: `in` | `out`.

### Examples

```
__ZOOM__in
__ZOOM__out
```

### Emission Condition

Emitted by `AiController` when the final `ChatStreamChunk` has `ViewerZoom != null`. Emitted in the same trailing-event block as `__LOCATION__`. Both events may appear in the same final chunk if a zoom command references an active location (unusual but not prohibited).

### Frontend Handling

Parsed in `aiApi.ts` (same parser that handles `__LOCATION__`). On match:
- `direction === 'in'`: call `viewerEngine.zoomBy('in')`
- `direction === 'out'`: call `viewerEngine.zoomBy('out')`

No store update — zoom is a transient viewer command, not persisted state.

---

## Viewer Engine Interface Contract

Methods added to the viewer engine interface (`IViewerEngine` or equivalent):

### `fitBounds(ne, sw)`

```ts
fitBounds(
  ne: { lat: number; lng: number },
  sw: { lat: number; lng: number }
): void
```

Moves the camera so the bounding box defined by `ne` and `sw` fits within the viewport. The camera heading and tilt are preserved (or reset to defaults if not set). The transition animates smoothly.

**Behaviour**:
- Computes the geographic centre `{ lat: (ne.lat + sw.lat) / 2, lng: (ne.lng + sw.lng) / 2 }`
- Derives altitude from the larger angular span (latitude or longitude) and the viewport pixel height, using the map's projection
- Calls `map.moveCamera({ center, altitude })` with animation enabled

**Pre-conditions**: The Google Maps 3D map must be initialized and the `WebGLOverlayView` must be registered.

**Error handling**: If the map is not ready, logs a warning and returns without throwing.

---

### `zoomToAltitude(altitudeMetres)`

```ts
zoomToAltitude(altitudeMetres: number): void
```

New method for fallback zoom when no bounding box is available. Animates camera to the given altitude above the current centre point.

**Clamp**: `altitudeMetres` is clamped to `[50, 500_000]`.

---

### `zoomBy(direction)`

```ts
zoomBy(direction: 'in' | 'out'): void
```

Adjusts altitude by a factor of 0.5 (in) or 2.0 (out) from the current altitude, clamped to `[50, 500_000]` metres. Animates smoothly.

---

## Backend Interface Contract

### `ILocationResolutionService` changes

No interface signature change. The return type `ConfirmedLocationData?` grows two new optional fields (`LocationType`, `Viewport`). All existing callers receive the new fields transparently.

### `IGeocodingProvider` changes

`GeocodingCandidate` grows two new optional fields (`LocationType`, `Viewport`). The interface method signature `Task<IReadOnlyList<GeocodingCandidate>> SearchAsync(string query, CancellationToken ct)` is unchanged.

### `ViewerZoomDetector` interface

```csharp
public interface IViewerZoomDetector
{
    ViewerZoomCommand? Detect(string message);
}
```

Registered as `Transient` in DI. `SendChatMessageCommandHandler` receives it via constructor injection and calls it in parallel with the location resolution task.

---

## Marker Style Selector Contract

### `useMarkerStyleStore` (Zustand)

```ts
interface MarkerStyleState {
  markerStyle: 'pulsing-ring' | 'classic-pin' | '3d-highlight' | 'simple-dot'
  setMarkerStyle: (style: MarkerStyle) => void
}
```

The store reads from `localStorage` key `viewer.markerStyle` on initialization (inside `getDefaultMarkerStyle()` wrapped in try/catch). Writes on `setMarkerStyle`. Default: `'pulsing-ring'`.

### Marker Style Selector Component

Rendered inside the viewer control panel (the panel that already contains `RotationToggleButton`).

```ts
interface MarkerStyleSelectorProps {
  // no required props — reads/writes useMarkerStyleStore directly
}
```

Renders a segmented control or dropdown listing all four styles. Calls `setMarkerStyle` on change. The `POIMarkerOverlay` reads from the same store on each style change and re-renders the marker with the new geometry.
