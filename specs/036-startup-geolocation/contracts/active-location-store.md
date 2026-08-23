# Contract: Active Location Store

**Spec**: 036-startup-geolocation | **Date**: 2026-08-23

## Purpose

`activeLocationStore` is the session-scoped Zustand store that holds the currently active location for the Ask Lucy workspace. It is the single authoritative source for viewer centering, weather widget data, and location name display, regardless of how the location was established.

## Location: `src/store/activeLocationStore.ts`

## Interface

```typescript
import { create } from 'zustand'

type ActiveLocationSource = 'geolocation' | 'agent'

interface ActiveLocationState {
  source: ActiveLocationSource | null
  latitude: number | null
  longitude: number | null
  locationName: string | null
  confidence: number | null
}

interface ActiveLocationActions {
  setFromGeolocation(latitude: number, longitude: number): void
  setFromAgent(latitude: number, longitude: number, locationName: string, confidence: number): void
  setLocationName(latitude: number, longitude: number, locationName: string): void
  clear(): void
}

export const useActiveLocationStore = create<ActiveLocationState & ActiveLocationActions>()(
  // No persist — session-scoped only (spec.md Assumptions)
  (set, get) => ({
    source: null,
    latitude: null,
    longitude: null,
    locationName: null,
    confidence: null,

    setFromGeolocation(latitude, longitude) {
      // FR-012: no-op when an agent-confirmed location is active
      if (get().source === 'agent') return
      set({ source: 'geolocation', latitude, longitude, confidence: null })
    },

    setFromAgent(latitude, longitude, locationName, confidence) {
      set({ source: 'agent', latitude, longitude, locationName, confidence })
    },

    setLocationName(latitude, longitude, locationName) {
      const s = get()
      // Guard: only apply if coordinates still match current active location
      if (s.latitude !== latitude || s.longitude !== longitude) return
      set({ locationName })
    },

    clear() {
      set({ source: null, latitude: null, longitude: null, locationName: null, confidence: null })
    },
  }),
)
```

## Consumers

| Consumer | How it reads |
|----------|-------------|
| `ViewerSurface` | `useActiveLocationStore(s => ({ lat: s.latitude, lon: s.longitude, source: s.source }))` — replaces `GeolocationState` prop |
| `LocationWeatherWidget` | `useActiveLocationStore(s => ({ lat: s.latitude, lon: s.longitude }))` — replaces direct lat/lon props |
| `useCurrentWeather` | Receives lat/lon from `LocationWeatherWidget` (unchanged call signature) |

## Writers

| Writer | When |
|--------|------|
| `ChatPage` useEffect | When `geolocation.status === 'granted'` → `setFromGeolocation(lat, lon)` |
| `ChatPage` useEffect | When `geolocation.status === 'unavailable'` → `clear()` |
| `useChatStream` | When `ChatStreamEvent.type === 'location'` → `setFromAgent(lat, lon, name, confidence)` |
| `useCurrentWeather` / `LocationWeatherWidget` | On successful fetch → `setLocationName(lat, lon, snapshot.locationName)` |

## Invariants

1. `source === 'agent'` → `setFromGeolocation` is always a no-op (FR-012)
2. `setLocationName` only applies when coordinates still match — prevents stale weather responses overwriting a newer location's name
3. No `persist` middleware — state is lost on page reload (session-scoped per spec assumption)
4. `clear()` resets all fields including `source`, allowing geolocation to re-establish on revocation recovery
