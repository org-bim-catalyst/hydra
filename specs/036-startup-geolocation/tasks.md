# Tasks: Startup Geolocation and Live Location Context

**Input**: Design documents from `specs/036-startup-geolocation/`

**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅ | quickstart.md ✅

**Tests**: Included — spec.md acceptance criteria require automated test coverage for all main flows.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (US1–US4)

## Path Conventions

Frontend root: `src/AskLucy.Web/ClientApp/src/`  
Backend root: `src/AskLucy.Application/` | `src/AskLucy.Api/`

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: Create the shared `ActiveLocation` store that every user story depends on. No user story work can begin until this phase is complete.

**⚠️ CRITICAL**: All user stories share `activeLocationStore`. Complete this phase before any other.

- [X] T001 Create `src/AskLucy.Web/ClientApp/src/store/activeLocationStore.ts` implementing `ActiveLocationState` + `ActiveLocationActions` per `contracts/active-location-store.md` — include `setFromGeolocation` (no-op when `source === 'agent'`), `setFromAgent`, `setLocationName` (coordinates-match guard), and `clear`; no `persist` middleware
- [X] T002 [P] Create `src/AskLucy.Web/ClientApp/src/store/activeLocationStore.test.ts` — unit tests for all state transitions per `data-model.md` state-transition table: `setFromGeolocation` no-ops when `source === 'agent'` (FR-012 priority rule), `setFromAgent` always wins, `setLocationName` ignores stale coordinates, `clear` resets all fields

**Checkpoint**: `activeLocationStore` exists and all unit tests pass — user story implementation can begin.

---

## Phase 2: User Story 1 — Startup Detects and Loads Current Location (Priority: P1) 🎯 MVP

**Goal**: When the user opens the app and grants location permission, the viewer centers on their position and the temperature widget + location name display both update — within 5 seconds.

**Independent Test**: Open the app with location services available, grant permission, verify viewer centers and widgets update within 5 s. See quickstart.md Scenario 1.

### Implementation for User Story 1

- [X] T003 [US1] Update `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useGeolocation.ts` — change `GEOLOCATION_TIMEOUT_MS` to `15_000`; add a high-accuracy `getCurrentPosition({ enableHighAccuracy: true, timeout: 3_000 })` attempt before the existing `watchPosition({ enableHighAccuracy: false, timeout: 15_000 })`; on high-accuracy success commit to `activeLocationStore.setFromGeolocation()` immediately; on failure (any error) silently skip to the `watchPosition` result — retain `watchPosition` for mid-session revocation detection (research.md Decision 4)
- [X] T004 [P] [US1] Update `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useGeolocation.test.ts` — add tests: high-accuracy attempt succeeds → result used immediately; high-accuracy times out after 3 s → low-accuracy `watchPosition` result used; 15 s overall timeout with no position → `status` transitions to `'unavailable'`; tests use `vi.useFakeTimers()` + `navigator.geolocation` mocks per existing test patterns
- [X] T005 [US1] Update `src/AskLucy.Web/ClientApp/src/features/chat/pages/ChatPage.tsx` — add `useEffect` that writes geolocation to `activeLocationStore`: when `geolocation.status === 'granted'` call `activeLocationStore.setFromGeolocation(geolocation.latitude!, geolocation.longitude!)`; when `'unavailable'` call `activeLocationStore.clear()`; keep existing `const geolocation = useGeolocation()` instantiation unchanged
- [X] T006 [US1] Refactor `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` — remove `geolocation: GeolocationState` prop; instead read `{ source, latitude, longitude }` from `useActiveLocationStore`; update the `useEffect` condition: `source !== null && latitude !== null && longitude !== null` (was `geolocation.status === 'granted'`) for adding/zooming the GIS layer; `source === null && contentMode === 'map'` (was `status === 'unavailable'`) for reverting to placeholder; export `ViewerSurfaceProps` without `geolocation` field
- [X] T007 [US1] Refactor `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.tsx` — remove `latitude`/`longitude` props; instead read `{ latitude, longitude, locationName }` from `useActiveLocationStore`; use `activeLocationStore.setLocationName(lat, lon, snapshot.locationName)` inside the weather-success callback so the store's `locationName` stays in sync; update `ChatPage.tsx` call site to remove the now-unused prop pass
- [X] T008 [US1] Update `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.test.tsx` — update test setup to use `activeLocationStore` state instead of prop-driven lat/lon; add tests: (a) weather API success → `activeLocationStore.locationName` updated to response's `locationName` field; (b) weather API success with empty/null `locationName` → `activeLocationStore.locationName` set to `"${latitude}, ${longitude}"` fallback (SC-005); (c) weather API failure → widget shows stale badge without crashing, `activeLocationStore.locationName` retains previous value

**Checkpoint**: Open app, grant location permission → viewer centers, temperature widget shows weather, location name shows place name. US1 independently testable per quickstart.md Scenario 1.

---

## Phase 3: User Story 2 — Permission Denied or Unavailable (Priority: P1)

**Goal**: If the user denies location permission (or detection times out), the app loads gracefully with neutral placeholder states — no crash, no blocked UI.

**Independent Test**: Block location permission in browser settings before opening the app. Verify neutral states appear and the app is fully usable. See quickstart.md Scenarios 2 & 3.

### Implementation for User Story 2

- [X] T009 [US2] Verify `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.tsx` — confirm the refactored component (T006) renders `PlaceholderRenderTarget` when `activeLocationStore.source === null`; no additional changes needed if T006 correctly handles the null case
- [X] T010 [P] [US2] Verify `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.tsx` — confirm the refactored component (T007) renders nothing / placeholder when `activeLocationStore.latitude === null`; add dedicated test in `LocationWeatherWidget.test.tsx`: when `activeLocationStore` has `source: null`, widget renders null/placeholder with no API call issued
- [X] T011 [P] [US2] Add timeout-fallback test to `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useGeolocation.test.ts` — simulate geolocation hanging for 15 s using `vi.useFakeTimers()`; assert `status` transitions to `'unavailable'` at exactly the 15 s mark; assert `activeLocationStore.clear()` is called (mock the store or use the real store in the test)
- [X] T012 [US2] Manual verification checklist in `quickstart.md` Scenarios 2 & 3 — run both scenarios in Chrome with DevTools location override; confirm no console errors, no blank screen, no infinite spinners; record result in the spec as an acceptance checkpoint

**Checkpoint**: App loads correctly whether location is granted, denied, or times out. US2 independently testable without US3.

---

## Phase 4: User Story 3 — Agent-Confirmed Location Replaces Active Location (Priority: P1)

**Goal**: When Lucy's agent resolves a location with confidence, the viewer reframes and the widgets update — identical outcome to startup detection.

**Independent Test**: Ask Lucy "Find Al Safa 2 Park, Dubai". After Lucy confirms, verify viewer centers on Al Safa 2 Park and widgets reflect that location. See quickstart.md Scenarios 5 & 6.

### Implementation for User Story 3

- [X] T013 [US3] Extend `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.ts` — add `LOCATION_EVENT_PREFIX = '__LOCATION__'` constant; add `{ type: 'location'; latitude: number; longitude: number; locationName: string; confidence: number; source: string }` to the `ChatStreamEvent` union; add parser branch in `streamChat` generator after the existing `__MEMORY__` branch per `contracts/location-sse-event.md`
- [X] T014 [P] [US3] Add `__LOCATION__` SSE event parsing test in `src/AskLucy.Web/ClientApp/src/features/chat/api/aiApi.test.ts` (or create if absent) — mock a `fetch` response whose body contains a `__LOCATION__{…}` data line; collect all events from `streamChat`; assert exactly one event has `type === 'location'` with correct `latitude`, `longitude`, `locationName`, `confidence`, `source` fields; assert `content` events before it are unaffected
- [X] T015 [US3] Update `src/AskLucy.Web/ClientApp/src/features/chat/hooks/useChatStream.ts` — in the event-dispatch loop, handle `event.type === 'location'`: call `useActiveLocationStore.getState().setFromAgent(event.latitude, event.longitude, event.locationName, event.confidence)`; ensure this call happens synchronously in the generator loop (same pattern as the `'memory'` event handling)
- [X] T016 [P] [US3] Add agent-confirmation integration test in `src/AskLucy.Web/ClientApp/src/features/viewer/components/ViewerSurface.test.tsx` (or create) — set up `activeLocationStore` with a geolocation source; simulate `setFromAgent()` call with different coordinates; assert viewer re-centers on the new coordinates
- [X] T017 [US3] Add backend `__LOCATION__` SSE emission to the chat streaming handler — locate the SSE writing code path in `src/AskLucy.Api/` (likely `AiController.cs` or a streaming middleware); after flushing all content delta events, check if the agent execution result contains a `ResolvedLocation` (spec 035 output shape); if present and `Confidence >= ConfidenceThreshold`, serialize and emit `data: __LOCATION__{json}\n\n` per `contracts/location-sse-event.md`; validate `latitude` (−90 to 90) and `longitude` (−180 to 180) before emission; log a Warning and skip emission if invalid
- [X] T018 [US3] Add priority-rule integration test to `src/AskLucy.Web/ClientApp/src/store/activeLocationStore.test.ts` — call `setFromAgent(lat1, lon1, name, conf)`, then `setFromGeolocation(lat2, lon2)`; assert store still has `latitude === lat1` and `source === 'agent'` (quickstart.md Scenario 6)

**Checkpoint**: Lucy confirms a location → viewer reframes → widgets update. Priority rule enforced: agent location cannot be displaced by startup detection.

---

## Phase 5: User Story 4 — Location Name and Temperature Stay in Sync (Priority: P2)

**Goal**: The temperature widget and location name display are always consistent with the active location; weather data refreshes on location change only (no background timer).

**Independent Test**: Load a location via startup, verify widget sync; change the active location (via agent confirmation or second startup), verify both widgets update together. No weather re-fetch after 15+ minutes at the same location. See quickstart.md Scenarios 4, 7, 9.

### Implementation for User Story 4

- [X] T019 [US4] Update `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useCurrentWeather.ts` — remove `refetchInterval: WEATHER_REFETCH_INTERVAL_MS` from the `useQuery` call; keep `staleTime: WEATHER_REFETCH_INTERVAL_MS` and `placeholderData: keepPreviousData`; remove the `WEATHER_REFETCH_INTERVAL_MS` constant if no longer referenced (keep `STALE_AFTER_MS` for the stale-badge logic)
- [X] T020 [P] [US4] Update `src/AskLucy.Web/ClientApp/src/features/viewer/hooks/useCurrentWeather.test.ts` — add tests: (a) mock `weatherApi.getCurrentWeather`; advance timers by 30+ minutes; assert it was called exactly once (no time-based refetch per research.md Decision 5); (b) change lat/lon — assert it is called again with the new coordinates; (c) unchanged lat/lon — assert no additional call
- [X] T021 [P] [US4] Add sync-consistency test in `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.test.tsx` — render `LocationWeatherWidget` backed by `activeLocationStore`; mock weather API response; assert displayed location name matches `store.locationName` after the weather response arrives; change the store's location; assert the widget immediately reflects the new lat/lon query (new fetch triggered)

**Checkpoint**: All four user stories are complete and independently testable. The active location drives the viewer, weather widget, and location name display in sync, from either source, with the correct priority order.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Accessibility, error-path hardening, and final quickstart validation.

- [X] T022 [P] Add accessibility test `src/AskLucy.Web/ClientApp/src/features/viewer/components/LocationWeatherWidget.a11y.test.tsx` — use `jest-axe` to assert no axe violations in: loading state (null location), populated state (location name + temperature), stale state ("Last known reading" badge)
- [X] T023 Verify no silent failures: audit all new `async` code paths in `useGeolocation.ts`, `useChatStream.ts` (location event handler), and `activeLocationStore.ts` for unhandled rejections or swallowed errors per constitution §2.VIII; ensure every error path reaches the store's `clear()` or a user-visible neutral state
- [X] T024 [P] Run full quickstart.md validation suite — execute all 9 scenarios from `specs/036-startup-geolocation/quickstart.md` manually (Scenarios 1–3, 5–6) and via automated tests (Scenarios 4, 7–9); record pass/fail per scenario; all scenarios must pass before closing the feature

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: No dependencies — start immediately. Blocks all phases.
- **US1 (Phase 2)**: Depends on Phase 1 (activeLocationStore). Can start immediately after.
- **US2 (Phase 3)**: Depends on Phase 1 + US1 (T005, T006, T007 must be complete — US2 verifies the failure paths of the same wiring).
- **US3 (Phase 4)**: Depends on Phase 1 only. Can be worked in parallel with US1 after Phase 1.
- **US4 (Phase 5)**: Depends on US1 (T007 wires weather) and US3 (T015 wires agent events). Must come after both.
- **Polish (Phase 6)**: Depends on all user story phases.

### User Story Dependencies

- **US1 (P1)**: Start after Phase 1 — no story dependencies
- **US2 (P1)**: Start after US1 (T006, T007 must exist to verify their null-state rendering)
- **US3 (P1)**: Start after Phase 1 — independent of US1 and US2
- **US4 (P2)**: Start after US1 (T007 complete) and US3 (T015 complete)

### Within Each User Story

- Store/hook changes before component changes
- Frontend parse before backend emit (for US3 — frontend can be tested with mocked SSE independently)
- Tests can be written in parallel with implementation (marked [P])

### Parallel Opportunities

- T002 can run alongside T001 (same file — write tests as you write the store)
- T003 and T004 — implementation and tests for `useGeolocation` run in parallel
- T009 and T010 and T011 — all US2 verification tasks can run in parallel
- T013 and T014 — parse code + parse test for `aiApi.ts` in parallel
- T016 and T018 — viewer test and store priority test independent
- T019, T020, T021 — US4 hook change, hook test, widget test all parallel
- T022 and T023 — accessibility test and error-path audit parallel

---

## Parallel Example: User Story 3

```bash
# Launch all US3 tasks that can start together after Phase 1:
Task T013: Add __LOCATION__ SSE event to aiApi.ts
Task T014: Write __LOCATION__ SSE parsing test in aiApi.test.ts
Task T016: Add ViewerSurface agent-confirmation integration test

# Then sequentially:
Task T015: Handle 'location' event in useChatStream.ts (depends on T013)
Task T017: Add backend SSE emission to AiController.cs (independent of frontend)
Task T018: Add priority-rule test to activeLocationStore.test.ts (independent)
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1: Foundational (T001–T002)
2. Complete Phase 2: US1 (T003–T008)
3. **STOP and VALIDATE**: Grant location permission → viewer centers, widgets update
4. US1 delivers the startup geolocation experience end-to-end

### Incremental Delivery

1. Phase 1 (Foundational) → store is ready
2. Phase 2 (US1) → startup geolocation works → validate Scenario 1
3. Phase 3 (US2) → graceful fallback confirmed → validate Scenarios 2–3
4. Phase 4 (US3) → agent location works → validate Scenarios 5–6
5. Phase 5 (US4) → sync and no-background-refresh confirmed → validate Scenarios 7, 9
6. Phase 6 (Polish) → full quickstart.md passes

### Parallel Team Strategy

After Phase 1:
- Developer A: US1 + US2 (startup detection path)
- Developer B: US3 (agent SSE path — frontend and backend)
- Both merge before US4, which synthesizes both paths

---

## Notes

- [P] tasks = different files, no incomplete-task dependencies; safe to run concurrently
- `activeLocationStore` has no `persist` middleware — session-only, reset on page reload
- The weather API backend proxy pattern is intentional and must NOT be moved client-side (constitution §8 — API key security; see research.md Decision 1)
- T017 (backend SSE emission) depends on spec 035's `ResolvedLocation` output shape existing in the codebase; if spec 035 is not yet implemented, stub with a hardcoded test shape and replace when spec 035 lands
- All new `async` paths must be awaited or caught — no fire-and-forget (constitution §2.VIII)
- Commit after each phase checkpoint; do not merge unless the phase's checkpoint test passes
