# Data Model: Startup Geolocation and Live Location Context

**Branch**: `036-startup-geolocation` | **Date**: 2026-08-23

---

## Client-Side State

### `ActiveLocationState` (Zustand store — session-scoped, no persistence)

The single source of truth for the current active location across the entire session. Written by startup geolocation and agent-confirmed resolution; read by the viewer, temperature widget, and location name display.

```typescript
type ActiveLocationSource = 'geolocation' | 'agent'

interface ActiveLocationState {
  /** How the active location was established. null = no location set yet. */
  source: ActiveLocationSource | null

  latitude: number | null
  longitude: number | null

  /** Human-readable name. Populated by the weather API response (locationName field)
   *  for geolocation-sourced locations; by the agent's ResolvedLocation.name for
   *  agent-confirmed locations. null until first weather snapshot arrives. */
  locationName: string | null

  /** Agent-assigned confidence score. null for geolocation-sourced locations. */
  confidence: number | null
}

interface ActiveLocationActions {
  /** Sets the active location from device geolocation.
   *  NO-OP when source === 'agent' (FR-012 priority rule). */
  setFromGeolocation(latitude: number, longitude: number): void

  /** Sets the active location from an agent-confirmed resolution.
   *  Always wins — overrides any existing source including 'agent' (FR-012). */
  setFromAgent(
    latitude: number,
    longitude: number,
    locationName: string,
    confidence: number,
  ): void

  /** Sets the locationName once the weather API response arrives with it.
   *  Only applied when coordinates match the current active location (guards
   *  against a stale weather response landing after a location change). */
  setLocationName(latitude: number, longitude: number, locationName: string): void

  /** Resets to no-location state (e.g., permission revoked mid-session). */
  clear(): void
}
```

**State transitions:**

```
null ──► setFromGeolocation() ──► source='geolocation'
null ──► setFromAgent()       ──► source='agent'

source='geolocation' ──► setFromGeolocation() ──► source='geolocation' (update coords)
source='geolocation' ──► setFromAgent()       ──► source='agent'       (override)
source='agent'       ──► setFromGeolocation() ──► source='agent'       (NO-OP — FR-012)
source='agent'       ──► setFromAgent()       ──► source='agent'       (update)

any ──► clear() ──► source=null
```

---

### Existing Types (unchanged)

#### `GeolocationState` (from `useGeolocation.ts`)

```typescript
type GeolocationStatus = 'resolving' | 'granted' | 'unavailable'

interface GeolocationState {
  status: GeolocationStatus
  latitude: number | null
  longitude: number | null
}
```

`useGeolocation` continues to be instantiated once in `ChatPage` and drives `activeLocationStore.setFromGeolocation()` and `activeLocationStore.clear()`. `ViewerSurface` and `LocationWeatherWidget` are migrated from consuming `GeolocationState` props to consuming `activeLocationStore` directly.

#### `WeatherSnapshot` (from `weatherApi.ts` — unchanged)

```typescript
interface WeatherSnapshot {
  locationName: string
  temperatureCelsius: number
  condition: WeatherCondition
  isDaytime: boolean
  observedAtUtc: string  // ISO 8601
}

type WeatherCondition =
  | 'Clear' | 'PartlyCloudy' | 'Cloudy' | 'Fog'
  | 'Rain' | 'Snow' | 'Thunderstorm' | 'Windy'
```

The `locationName` field from `WeatherSnapshot` is used to populate `ActiveLocationState.locationName` for geolocation-sourced locations (via `activeLocationStore.setLocationName()`).

---

## SSE Stream Event

### `LocationChatStreamEvent` (new addition to `ChatStreamEvent` union in `aiApi.ts`)

Emitted as a trailing SSE event when the backend agent (spec 035) has resolved a location with sufficient confidence and the user has confirmed it. Carried as `__LOCATION__{json}` in the SSE stream, parallel to `__RAG__` and `__MEMORY__`.

```typescript
interface LocationChatStreamEvent {
  type: 'location'
  latitude: number
  longitude: number
  /** Human-readable name from the agent's ResolvedLocation.name */
  locationName: string
  /** Confidence score from the agent's resolution (0.0–1.0) */
  confidence: number
  /** Which geospatial source resolved this location */
  source: string
}
```

**SSE wire format** (backend emits):
```
data: __LOCATION__{"latitude":25.2048,"longitude":55.2708,"locationName":"Al Safa 2 Park, Dubai","confidence":0.97,"source":"nominatim"}
```

**Updated `ChatStreamEvent` union:**
```typescript
export type ChatStreamEvent =
  | { type: 'content'; delta: string }
  | { type: 'retrieval'; outcome: RagRetrievalOutcome; citations: Omit<Citation, 'id'>[]; error: string | null }
  | { type: 'memory'; messageId: string; outcome: MemoryRetrievalOutcome }
  | { type: 'location'; latitude: number; longitude: number; locationName: string; confidence: number; source: string }
```

---

## Validation Rules

| Field | Rule |
|-------|------|
| `latitude` | −90 ≤ latitude ≤ 90 (enforced by FR-008 coordinate validation in chat flow) |
| `longitude` | −180 ≤ longitude ≤ 180 (same) |
| `confidence` | 0.0–1.0; values below system threshold trigger disambiguation in spec 035, not emitted as `__LOCATION__` |
| `locationName` | Non-empty string; coordinates shown as fallback (`${lat}, ${lon}`) if empty |
| `source` | Non-empty string identifier of the geocoding service used |
