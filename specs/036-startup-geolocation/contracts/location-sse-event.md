# Contract: Location SSE Event (`__LOCATION__`)

**Spec**: 036-startup-geolocation (frontend consumer) / 035-location-discovery-viewer (backend emitter) | **Date**: 2026-08-23

## Purpose

When Lucy's agentic system resolves a location with sufficient confidence and the user confirms it (spec 035), the backend chat streaming handler emits a `__LOCATION__` trailing SSE data event. The frontend parses this event and applies it to `activeLocationStore` via `setFromAgent()`, triggering an atomic update of the viewer, temperature widget, and location name display.

## Wire Format

```
data: __LOCATION__<json-payload>\n\n
```

`<json-payload>` is a compact JSON object (no trailing whitespace, no line breaks inside):

```json
{
  "latitude": 25.2048,
  "longitude": 55.2708,
  "locationName": "Al Safa 2 Park, Dubai",
  "confidence": 0.97,
  "source": "nominatim"
}
```

## Field Definitions

| Field | Type | Constraints | Description |
|-------|------|-------------|-------------|
| `latitude` | `number` | −90 ≤ x ≤ 90 | WGS-84 latitude |
| `longitude` | `number` | −180 ≤ x ≤ 180 | WGS-84 longitude |
| `locationName` | `string` | Non-empty | Human-readable name from agent resolution |
| `confidence` | `number` | 0.0 ≤ x ≤ 1.0 | Agent confidence score (only emitted when above system threshold) |
| `source` | `string` | Non-empty | Geocoding service identifier (e.g., `"nominatim"`) |

## Positioning in the SSE Stream

The `__LOCATION__` event is a trailing event — it arrives after all `content` delta events but before `[DONE]`. It may arrive alongside or before `__MEMORY__`; the order between `__LOCATION__` and `__MEMORY__` is not guaranteed. Frontend parsing must handle both orderings.

```
data: I found Al Safa 2 Park in Dubai...    ← content deltas
data: ...                                    ← content deltas continue
data: __MEMORY__{"messageId":"...","outcome":"Found"}
data: __LOCATION__{"latitude":25.2048,"longitude":55.2708,"locationName":"Al Safa 2 Park, Dubai","confidence":0.97,"source":"nominatim"}
data: [DONE]
```

## Frontend Parse Location (addition to `aiApi.ts`)

```typescript
const LOCATION_EVENT_PREFIX = '__LOCATION__'

// Inside streamChat generator, in the event-type dispatch block:
if (data.startsWith(LOCATION_EVENT_PREFIX)) {
  const payload = JSON.parse(data.slice(LOCATION_EVENT_PREFIX.length)) as {
    latitude: number
    longitude: number
    locationName: string
    confidence: number
    source: string
  }
  yield {
    type: 'location',
    latitude: payload.latitude,
    longitude: payload.longitude,
    locationName: payload.locationName,
    confidence: payload.confidence,
    source: payload.source,
  }
  continue
}
```

## Backend Emit Location (addition to `AiController.cs` / chat streaming handler)

The backend emits this event immediately after the agent's text response has been flushed, when the agent execution result contains a `ResolvedLocation` with `Confidence >= ConfidenceThreshold`:

```csharp
// In the SSE-writing code path, after flushing content deltas:
if (resolvedLocation is not null)
{
    var locationPayload = JsonSerializer.Serialize(new
    {
        latitude = resolvedLocation.Latitude,
        longitude = resolvedLocation.Longitude,
        locationName = resolvedLocation.Name,
        confidence = resolvedLocation.Confidence,
        source = resolvedLocation.Source,
    });
    await writer.WriteAsync($"data: __LOCATION__{locationPayload}\n\n");
    await writer.FlushAsync();
}
```

## Error Handling

- If the `__LOCATION__` JSON payload cannot be parsed, the event is discarded silently (per the no-silent-failure principle, the text confirmation already appeared in the chat, so the user knows the location was found; a parse failure is a degraded experience, not a user-visible error)
- If `latitude` or `longitude` are out of range, `setFromAgent` is not called; the malformed event is logged at Warning level server-side before emission is attempted (backend validation gate)
- The frontend never emits this event itself; it is always server-originated
