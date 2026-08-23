# Implementation Plan: Startup Geolocation and Live Location Context

**Branch**: `036-startup-geolocation` | **Date**: 2026-08-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/036-startup-geolocation/spec.md`

## Summary

Wire device geolocation and agent-confirmed locations into a unified `ActiveLocation` session state so the 3D viewer, temperature widget, and location name display always reflect the same site — regardless of whether the location was detected at startup or resolved by Lucy's agentic system (spec 035). The core geolocation subsystem (hook, weather widget, viewer integration) already exists; this feature adds the high-accuracy-first fallback, extends the timeout to 15 s, introduces the shared `activeLocationStore` that agent confirmations write to, the priority rule (agent > geolocation), and removes the time-based weather refresh in favour of location-change-driven fetches.

## Technical Context

**Language/Version**: TypeScript 5 / React 19 (frontend); C# 12 / .NET 10 (backend)

**Primary Dependencies**: React 19, MUI v6, Zustand 4, TanStack Query 5, Vitest 4 + RTL + msw (frontend); ASP.NET Core 10, MediatR, EF Core 10 (backend — weather endpoint already exists)

**Storage**: Session-only client-side Zustand store (no persistence); backend weather API proxy reads from upstream weather provider, stores nothing

**Testing**: Vitest + jsdom + `@testing-library/react` + msw (frontend unit/integration); Playwright (E2E); xUnit (backend — weather controller already covered)

**Target Platform**: Modern web browser (Chrome, Firefox, Safari); ASP.NET Core 10 server on Windows/Linux

**Performance Goals**: Viewer + widgets reflect startup location within 5 s of permission grant (SC-001); agent-confirmed location reflected within the same interaction turn (SC-002)

**Constraints**: 15 s hard geolocation timeout (matching geocoding timeout in spec 035); agent-confirmed location is higher priority than startup detection and cannot be displaced by it (FR-012); weather data fetched once per location change, no time-based background refresh (FR-007); device coordinates transmitted to backend only as transient lookup parameters, never stored (FR-011 intent — see research.md Decision 1)

**Scale/Scope**: Single session-scoped active location; no multi-user sharing; single `ChatPage` consumer

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Gate | Status | Notes |
|------|--------|-------|
| Clean Architecture — dependency rule (§3) | ✅ Pass | `activeLocationStore` is frontend state; no new backend assembly cross-references |
| No silent failures (§2.VIII) | ✅ Pass | Geolocation denial/timeout → neutral state, never swallowed; all error paths covered in FR-004/FR-005 |
| SOLID / OCP (§2.II) | ✅ Pass | Geocoding agent tool (spec 035) is a new `IAgentTool` class; no edits to the agent runtime |
| Dependency inversion (§2.V) | ✅ Pass | `activeLocationStore` consumed by viewer, weather widget via store interface; no direct coupling between components |
| No implementation details in domain (§3) | ✅ Pass | Weather proxy pattern keeps API key server-side; constitutionally required (§8) |
| Security — secrets not in client bundle (§8) | ✅ Pass | Weather and geocoding API keys remain server-side through backend proxy pattern |
| Provider neutrality (§9) | ✅ Pass | No LLM provider assumption; geocoding tool uses `IAgentTool` abstraction |
| Accessibility WCAG 2.1 AA (§7) | ⚠️ Required | Geolocation permission prompt is native browser; loading states and location name display must be accessible |
| Test coverage (§10) | ⚠️ Required | `useGeolocation`, `activeLocationStore`, `streamChat` location event parsing, `ViewerSurface` priority logic all need test coverage |

## Project Structure

### Documentation (this feature)

```text
specs/036-startup-geolocation/
├── plan.md              ← this file
├── research.md          ← Phase 0 decisions
├── data-model.md        ← Phase 1 entities and state shapes
├── quickstart.md        ← Phase 1 validation guide
├── contracts/
│   ├── active-location-store.md   ← store interface contract
│   └── location-sse-event.md      ← SSE __LOCATION__ event contract
└── tasks.md             ← Phase 2 (/speckit-tasks)
```

### Source Code

```text
src/AskLucy.Web/ClientApp/src/

features/viewer/
├── hooks/
│   ├── useGeolocation.ts              ← MODIFY: 15 s timeout, high-accuracy-first + fallback
│   ├── useGeolocation.test.ts         ← MODIFY: add timeout/accuracy-fallback tests
│   ├── useCurrentWeather.ts           ← MODIFY: remove refetchInterval
│   └── useCurrentWeather.test.ts      ← MODIFY: verify no time-based refetch
├── components/
│   ├── ViewerSurface.tsx              ← MODIFY: consume activeLocationStore instead of raw GeolocationState
│   ├── LocationWeatherWidget.tsx      ← MODIFY: consume activeLocationStore instead of raw lat/lon
│   └── LocationWeatherWidget.test.tsx ← MODIFY: add agent-confirmed location tests

features/chat/
├── api/
│   └── aiApi.ts                       ← MODIFY: add __LOCATION__ SSE event to ChatStreamEvent union + parser
├── hooks/
│   └── useChatStream.ts               ← MODIFY: handle 'location' event → activeLocationStore.setFromAgent()
└── pages/
    └── ChatPage.tsx                   ← MODIFY: wire geolocation → activeLocationStore.setFromGeolocation()

store/
└── activeLocationStore.ts             ← NEW: unified active location Zustand store

tests (co-located *.test.ts):
├── store/activeLocationStore.test.ts  ← NEW: priority rule + state transitions
└── features/chat/api/aiApi.test.ts    ← MODIFY: add __LOCATION__ event parsing test
```

Backend (weather endpoint already implemented; SSE streaming handler requires one addition for spec 036):

```text
src/AskLucy.Application/
└── (no changes — weather query handler exists; geocoding agent tool is spec 035)

src/AskLucy.Infrastructure/
└── (no changes)

src/AskLucy.Api/
└── AiController.cs (or SSE streaming handler)  ← MODIFY: emit __LOCATION__ SSE event
    when agent execution result contains a ResolvedLocation with Confidence ≥ threshold
    (see contracts/location-sse-event.md for wire format and validation rules)
```

## Complexity Tracking

No constitution violations requiring justification. The weather API backend proxy (coordinates sent transiently to backend for lookup, not stored) is the constitutionally correct pattern for API key security (§8). This supersedes the "client-side only" spec clarification, which was given without architectural context — see research.md Decision 1.
