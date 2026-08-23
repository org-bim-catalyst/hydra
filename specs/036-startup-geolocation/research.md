# Research: Startup Geolocation and Live Location Context

**Branch**: `036-startup-geolocation` | **Date**: 2026-08-23

---

## Decision 1 — Backend Weather Proxy vs. Client-Side Direct Fetch

**Decision**: Retain the existing backend proxy pattern for weather and reverse geocoding. The frontend calls `/api/v1/weather/current?latitude={lat}&longitude={lon}`; the backend makes the upstream provider call server-side and returns `WeatherSnapshot`. Coordinates are transmitted to the backend transiently as query parameters and are NOT stored in any database record.

**Rationale**: The spec's Q2 clarification ("client-side only") was provided without knowledge of the existing architecture. If the frontend called the weather/geocoding provider directly, API keys would be exposed in the browser bundle — a constitution §8 violation ("Secrets never live in source control, client bundles, or logs"). The spirit of FR-011 (no passive storage of raw coordinates) is fully satisfied by the backend proxy pattern: coordinates are never persisted; they are query parameters that route a lookup. This decision supersedes the spec's "client-side only" framing.

**Alternatives considered**:
- Client-side direct fetch — rejected: exposes API keys in the browser bundle (§8 violation)
- Store coordinates in a session table — rejected: FR-011 explicitly forbids backend storage

---

## Decision 2 — Unified Active Location: Zustand Store

**Decision**: Introduce a new session-scoped Zustand store `activeLocationStore` (`src/store/activeLocationStore.ts`, no `persist` middleware) as the single source of truth for the current active location. Both startup geolocation and agent-confirmed resolution write to this store. `ViewerSurface` and `LocationWeatherWidget` read from it.

**Rationale**: The current architecture threads `GeolocationState` as a prop from `ChatPage` down to `ViewerSurface` and `LocationWeatherWidget`. Adding a second source (agent confirmation) via prop threading would require `ChatPage` to merge two sources and propagate a synthesized shape — increasing coupling. A Zustand store matches the existing pattern for session-scoped state without server mirroring (`workspaceOverlayStore`, `viewerEngineStore`) and cleanly separates the two write paths from the read consumers.

**Alternatives considered**:
- Extend `GeolocationState` with an `agentOverride` field — rejected: conflates two concerns in one hook; `useGeolocation` should remain focused on device position only
- Thread from `ChatPage` via React context — rejected: less discoverable than a store; context re-renders are broader

---

## Decision 3 — Agent Location via SSE `__LOCATION__` Event

**Decision**: When the backend agent (spec 035) resolves a confident location, the chat streaming handler emits a trailing `__LOCATION__{…}` SSE data event alongside the text confirmation. The frontend's `streamChat` generator yields a `{ type: 'location', … }` event; `useChatStream` handles it by calling `activeLocationStore.setFromAgent()`.

**Rationale**: This is consistent with the established `__RAG__` and `__MEMORY__` trailing event pattern already in `aiApi.ts`. It keeps structured location data separate from the text stream without requiring a parallel HTTP call, and it preserves the ordering guarantee (location event arrives after the agent's text confirmation has streamed).

**Alternatives considered**:
- Separate REST call after streaming ends — rejected: requires client to poll or re-fetch; loses the ordering guarantee
- Embed coordinates as JSON in the AI text response and parse with regex — rejected: fragile, parse errors would silently fail (§2.VIII violation)
- WebSocket push from server — rejected: existing SSE streaming is sufficient; no infrastructure addition justified

---

## Decision 4 — High-Accuracy-First Geolocation with Low-Accuracy Fallback

**Decision**: Replace the current single `watchPosition({ enableHighAccuracy: false, timeout: 10_000 })` call with a two-phase approach: (1) `getCurrentPosition({ enableHighAccuracy: true, timeout: 3_000 })` attempted first; on success, the result is committed to `activeLocationStore` and a `watchPosition({ enableHighAccuracy: false, timeout: 15_000 })` is started for mid-session revocation detection; on failure (timeout or error), step (1) is silently skipped and only the `watchPosition` low-accuracy result is used.

**Rationale**: High-accuracy GPS typically resolves in < 1 s on a clear-sky mobile device and falls back automatically in < 3 s — keeping the total time well inside SC-001's 5-second target. `watchPosition` is retained for revocation detection (existing behaviour per spec 027). The inner 3 s timeout for the high-accuracy attempt leaves 12 s of the 15 s total budget for the low-accuracy fallback.

**Alternatives considered**:
- Single `watchPosition({ enableHighAccuracy: true })` — rejected: GPS on mobile can take 30+ s indoors; would reliably miss SC-001
- Always use low accuracy — rejected: spec FR-013 explicitly requires high-accuracy-first

---

## Decision 5 — Weather Refresh Policy: Location-Change-Driven Only

**Decision**: Remove `refetchInterval` from `useCurrentWeather`. TanStack Query's query key is `['weather', 'current', latitude, longitude]` — a coordinate change produces a different key, triggering a fresh fetch automatically. No time-based background refetch.

**Rationale**: Spec FR-007 / clarification Q5 is explicit: "no time-based background refresh." The query-key-driven behaviour already handles the "refresh on location change" requirement without any additional logic. `keepPreviousData` is retained so a location update doesn't briefly blank the widget while the new fetch is in flight.

**Alternatives considered**:
- Keep existing 15-min `refetchInterval` — rejected: directly contradicts spec clarification
- Manual cache invalidation on location change — rejected: unnecessary; TanStack Query's key-change mechanism already does this

---

## Decision 6 — Location Source Priority Rule Implementation

**Decision**: `activeLocationStore.setFromGeolocation(lat, lon)` is a no-op when `store.source === 'agent'`. This enforces FR-012: once an agent confirms a location, startup detection cannot displace it. The `source` field is reset to `null` only by `clear()`.

**Rationale**: A guard inside `setFromGeolocation` is simpler and easier to test than a merge in `ChatPage` or `ViewerSurface`. It localises the priority rule to the store, making it the single place where the invariant lives.

**Alternatives considered**:
- Priority enforced in `ChatPage` useEffect — rejected: duplicates logic in a consumer; harder to test in isolation
- Priority enforced in `ViewerSurface` — rejected: presentation component should not own business rules
