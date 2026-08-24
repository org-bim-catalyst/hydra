# Quickstart Validation Guide: Startup Geolocation and Live Location Context

**Branch**: `036-startup-geolocation` | **Date**: 2026-08-23

## Prerequisites

- Frontend dev server running (`npm run dev` in `src/AskLucy.Web/ClientApp`)
- Backend API running (`dotnet run` in `src/AskLucy.Api`)
- A modern browser with location services enabled (Chrome DevTools geolocation override recommended for repeatability)
- Vitest test runner available (`npm run test`)

## Scenario 1 — Startup Geolocation Loads the Viewer (SC-001, FR-001–FR-003, FR-013)

**Setup**: In Chrome DevTools → Sensors → override location to Dubai Marina (25.0819, 55.1367). Clear site data to simulate a fresh session.

**Steps**:
1. Open the app. The browser will show a location permission prompt.
2. Grant permission.

**Expected within 5 seconds**:
- The viewer (map) centers on Dubai Marina.
- The temperature widget shows weather conditions and a location name.
- The location name display shows a recognisable place name (not raw coordinates).
- No error dialog or loading spinner remains indefinitely.

**Verify loading state (FR-003)**:
- Before granting permission, the temperature widget and location name area show a loading/placeholder indicator.
- The viewer shows its neutral placeholder state.

---

## Scenario 2 — Permission Denied Falls Back Gracefully (SC-003, FR-004)

**Setup**: Block location permission in browser settings for the app origin before loading.

**Steps**:
1. Open the app. No permission prompt appears (already blocked).

**Expected within 15 seconds**:
- The viewer shows its neutral placeholder state.
- The temperature widget shows an empty/placeholder state (no weather data).
- The location name display shows an empty/placeholder state.
- No crash. No error dialog. User can interact with Lucy normally.

---

## Scenario 3 — Geolocation Timeout Falls Back Gracefully (FR-005)

**Setup**: Use Chrome DevTools → Sensors → set location mode to "No override" (which makes geolocation requests hang). Do NOT block permission — allow the prompt.

**Steps**:
1. Open the app. Grant the permission prompt.
2. Wait 15 seconds.

**Expected**:
- After exactly 15 seconds the app transitions to the neutral placeholder state (same as Scenario 2).
- No infinite spinner. No crash.

---

## Scenario 4 — High-Accuracy First, Low-Accuracy Fallback (FR-013)

**Automated test** (`useGeolocation.test.ts`):
- Mock `navigator.geolocation.getCurrentPosition` to simulate the high-accuracy call timing out (rejects after 3 s).
- Mock `navigator.geolocation.watchPosition` to return a low-accuracy fix.
- Assert that the low-accuracy fix is eventually committed to `activeLocationStore`.

---

## Scenario 5 — Agent-Confirmed Location Replaces Active Location (SC-002, FR-006, FR-012)

**Prerequisites**: Scenario 1 has run (a startup location is active: Dubai Marina).

**Steps**:
1. In the chat, ask Lucy: "Find Al Safa 2 Park, Dubai".
2. Lucy resolves the location and confirms it (per spec 035 flow — single confident match).

**Expected immediately after Lucy's confirmation**:
- The viewer recenters on Al Safa 2 Park (different from Dubai Marina).
- The temperature widget updates to Al Safa 2 Park's current conditions.
- The location name display updates to "Al Safa 2 Park, Dubai" (or equivalent).
- The transition is smooth — no full page reload, no flash of wrong content.

---

## Scenario 6 — Agent Location Wins Over In-Progress Startup (FR-012 priority rule)

**Automated test** (`activeLocationStore.test.ts`):
- Call `setFromAgent(25.2048, 55.2708, 'Al Safa 2 Park', 0.97)`.
- Then call `setFromGeolocation(25.0819, 55.1367)`.
- Assert that `store.latitude === 25.2048` and `store.source === 'agent'` (geolocation call was a no-op).

---

## Scenario 7 — Weather Does Not Refresh on a Timer (FR-007, research.md Decision 5)

**Automated test** (`useCurrentWeather.test.ts`):
- Render the hook with stable coordinates for 20+ minutes (simulated via `vi.advanceTimersByTime`).
- Assert that `weatherApi.getCurrentWeather` was called exactly once (on mount), not periodically.
- Change the coordinates to simulate a location change.
- Assert that `weatherApi.getCurrentWeather` is called exactly once more with the new coordinates.

---

## Scenario 8 — Location SSE Event Parsing (FR-006, data-model.md)

**Automated test** (`aiApi.test.ts`):
- Mock a `fetch` response whose body includes:
  ```
  data: __LOCATION__{"latitude":25.2048,"longitude":55.2708,"locationName":"Al Safa 2 Park, Dubai","confidence":0.97,"source":"nominatim"}
  ```
- Collect all yielded events from `streamChat(...)`.
- Assert that one event has `type === 'location'` with the correct fields.

---

## Scenario 9 — Location Name Display Never Shows Raw Coordinates (SC-005, FR-008)

**Steps**:
- Override geolocation to a position with a known place name.
- Wait for the weather API response to arrive.

**Expected**: The location name display shows the `locationName` from the weather response, not the raw coordinate string.

**Fallback test**: Mock the weather API to return a 500 error. Assert that the location name falls back to `"${latitude}, ${longitude}"` (not blank).

---

## Run All Unit Tests

```bash
# From src/AskLucy.Web/ClientApp:
npm run test -- --reporter=verbose
```

Key test files to watch:
- `src/store/activeLocationStore.test.ts` (new)
- `src/features/viewer/hooks/useGeolocation.test.ts` (modified)
- `src/features/viewer/hooks/useCurrentWeather.test.ts` (modified)
- `src/features/chat/api/aiApi.test.ts` (modified)
- `src/features/viewer/components/LocationWeatherWidget.test.tsx` (modified)

## References

- Store contract: [contracts/active-location-store.md](contracts/active-location-store.md)
- SSE event contract: [contracts/location-sse-event.md](contracts/location-sse-event.md)
- Data model: [data-model.md](data-model.md)
- Spec: [spec.md](spec.md)
