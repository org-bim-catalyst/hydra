# Implementation Plan: AI-to-UI Floating Panel Framework

**Branch**: `028-ai-floating-panels` | **Date**: 2026-08-17 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-ai-floating-panels/spec.md`

## Summary

Give Ask Lucy (and any future AI agent/tool) a way to present a visual response as an interactive,
draggable, resizable, semi-transparent floating panel over the Flumeria Three.js immersive viewer,
without hardcoding every possible panel type into the core viewer. A new, framework-agnostic
`viewer/panels/` package hosts a `PanelTypeRegistry` (developer-registered `typeKey → renderer + zod
schema`, per Clarifications Q1) and a session-scoped `floatingPanelStore` (Zustand) that owns open
panel instances — position, size, minimized/focus state, cascade placement for unpositioned requests
(Q2), and a fixed-cap/LRU-eviction policy (Q3). Panel requests arrive over a new per-user SignalR hub
(`PanelHub`, mirroring `AgentExecutionHub`), kept private to the triggering user (Q5). Drag/resize
chrome uses a new `react-rnd` dependency; panel data is validated at the boundary with `zod` (already
named in this repo's stated frontend stack but not yet used). A single new small backend aggregate,
`UserPanelPreference`, persists a bounded `[40, 100]` opacity preference (Q4), surfaced through a new
"Viewer" Settings tab and applied live to every open panel. Panels can reference viewer context (a
layer/element id) and both drive and react to the existing `ViewerEngine`/`ViewerEventBus` (spec 027),
so no second event system is introduced. This feature ships four built-in panel types (`chart`,
`table`, `summary`, `parameters`) as the registry's proof of extensibility, not an exhaustive catalog of
every category the spec lists.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript ~6.0 with React 19, Vite 8 (frontend,
`src/AskLucy.Web/ClientApp`)

**Primary Dependencies**:
- Frontend (existing, reused): `three` ^0.185.1, `@react-three/fiber` ^9.6.1, `zustand` ^5,
  `@tanstack/react-query` ^5, `@mui/material` ^9, `@microsoft/signalr` ^10, `d3` ^7 (charting)
- Frontend (new): `react-rnd` (drag + resize chrome, research.md Decision 3), `zod` (panel-data
  runtime validation, research.md Decision 4 — declared in `.claude/CLAUDE.md`'s stack but not yet
  installed anywhere in `ClientApp`)
- Backend (existing, reused): MediatR, FluentValidation, EF Core, SignalR (`AgentExecutionHub` et al.
  already registered in `Infrastructure/DependencyInjection.cs`)
- Backend (new): none — `PanelHub`/`UserPanelPreference` reuse existing registered infrastructure, no
  new NuGet package

**Storage**: SQL Server (existing). This feature adds exactly one new table, `UserPanelPreference`
(one row per user, `OpacityPercent`) — everything else (open panels, layout, viewer-context
association) is client-session state only (spec Assumption, data-model.md).

**Testing**: `vitest` + `@testing-library/react` + `jest-axe` (frontend unit/component/a11y, existing
convention); `xUnit` (backend unit/integration, existing convention, mirroring
`UserVoicePreference`'s test suite shape); Playwright `.spec.ts` (E2E, existing convention,
`tests/AskLucy.E2E.Tests`)

**Target Platform**: Web browser (desktop-first, matching the viewer's existing target — spec
Assumption) via the existing ASP.NET Core–hosted React SPA; no new deployment target.

**Project Type**: Web application (existing `AskLucy.Domain/Application/Infrastructure/Persistence/Web`
solution + `ClientApp` React frontend) — this feature adds to the existing structure, no new project.

**Performance Goals**: A requested panel appears within 2s of the AI response being ready (SC-001);
drag/resize interactions track the pointer with no perceptible lag; at least 5 (up to the 10-panel cap,
data-model.md) floating panels can be open simultaneously without the viewer becoming unresponsive
(SC-004); an opacity change applies to all open panels immediately, no reload (SC-005).

**Constraints**: Opacity is bounded `[40, 100]`, enforced at both the FluentValidation boundary and the
domain layer (Clarifications Q4); at most 10 concurrently open panels, least-recently-focused evicted
past that (Clarifications Q3/FR-022); panels are private per user — no cross-user push, no shared
viewer-session concept exists yet (Clarifications Q5/FR-023); every panel request/response MUST
produce a caller-visible outcome, never a silent no-op (constitution §2.VIII, FR-016/FR-017).

**Scale/Scope**: Single-user, session-scoped panel set per browser tab (no persistence beyond the
opacity preference); 4 user stories (1×P1 panel appears, 1×P2 layout management, 2×P3 opacity +
viewer-context communication); 4 built-in panel types shipped as the registry's proof of extensibility.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Assessment |
|---|---|
| I. Clean Architecture & Dependency Rule | PASS — `UserPanelPreference` follows `Domain → Application (CQRS) → Infrastructure (EF Core, SignalR) → Web (controller, hub)`, matching `UserVoicePreference` exactly; the panel registry/store/chrome are presentation-only client state with no business logic leaking server-side. |
| II. SOLID | PASS — `UserPanelPreference` is a new, separate aggregate rather than folded onto `UserVoicePreference` (research.md Decision 6, SRP); `PanelTypeDefinition` registration is the OCP seam — a new panel type is added, never an existing one edited. |
| III. Simplicity First (DRY/KISS/YAGNI) | PASS — reuses the existing `ViewerEventBus`/`ViewerEngine` command surface instead of a second event system (research.md Decision 7); reuses `AgentExecutionHub`'s exact hub pattern instead of inventing a new transport shape; the 4 built-in panel types are the minimum needed to prove the registry, not a built-out catalog of every spec-listed category (deferred per data-model.md/contracts, not built speculatively). |
| IV. Composition Over Inheritance | PASS — panel types are composed via the `PanelTypeDefinition` registry entry (renderer + schema as data), not a class hierarchy of panel subtypes. |
| V. Dependency Inversion & Testability | PASS — `IPanelNotifier` is defined in `Application`, mockable in handler unit tests with no live SignalR connection required; `UserPanelPreference` command/query handlers depend on `IUserPanelPreferenceRepository`/`IUnitOfWork` abstractions, not EF Core directly. |
| VI. Separation of Concerns | PASS — opacity validation/persistence lives in the MediatR command handler, not `PanelsController`; panel drag/resize/eviction logic lives in `floatingPanelStore`, not inside `FloatingPanel.tsx`'s render body. |
| VII. Convention Over Configuration | PASS — `PanelHub` mirrors `AgentExecutionHub`'s group-per-user/route-naming convention exactly; `UserPanelPreference`'s CQRS/entity/migration shape mirrors `UserVoicePreference`; `panelPreferencesStore.ts` mirrors `voicePreferencesStore.ts`'s persist/optimistic-update convention; the new Settings tab is appended (not inserted) to `SETTINGS_TAB_INDEX` to avoid renumbering existing tabs' references. |
| VIII. No Silent Failures (NON-NEGOTIABLE) | PASS — every `PanelRequested` push resolves to exactly one visible `FloatingPanel` state (`valid`/`invalid`/`unknown-type`), never dropped (contracts/panel-hub-events.md); every opacity save failure surfaces via the Settings tab's Snackbar (mirrors `voicePreferencesStore`'s `error` field), never console-only. |
| §6 API Standards | PASS — new `GET/PUT /api/v1/panels/preferences` is versioned, `[Authorize]`, rate-limited via a new `"panels-endpoints"` policy (mirroring `AiController`'s `"ai-endpoints"`/`WeatherController`'s `"weather-endpoints"`), returns RFC 7807 Problem Details on validation failure via the existing `ProblemDetailsMiddleware`, documented in OpenAPI automatically (attribute-routed MVC controller). |
| §7 UI Principles | PASS — `FloatingPanel` chrome uses MUI theming (light/dark, no hardcoded colors); the opacity slider and all panel controls (drag handle, resize handles, minimize/close) MUST meet WCAG 2.1 AA (keyboard operability, ARIA, focus states) — carried into tasks as an explicit requirement even though the business-facing spec doesn't restate it, since the constitution governs it unconditionally. |
| §8 Security | PASS — `PanelHub` requires `[Authorize]` and groups strictly by the server-verified user id (never client-supplied), enforcing FR-023's per-user privacy at the transport layer, not just in UI; panel `data` is untrusted AI/tool output rendered into the UI — every built-in renderer treats `data` as data (React's default escaping), never `dangerouslySetInnerHTML` (constitution §8 XSS rule). |
| §9 AI Principles | N/A / PASS — this feature stops at the panel-request *contract* (`IPanelNotifier`/`PanelHub`) and the rendering framework; it deliberately does not build the "AI decides to show a panel" reasoning step (spec Assumption), consistent with how spec 027 stopped at the viewer command/event contract for the same reason. |
| §14 Observability | PASS — `PanelHub`/`PanelsController` go through the same Serilog structured-logging + correlation-id pipeline as every other hub/controller in this codebase; no new observability gap introduced. |
| §15 Performance | PASS — panel count is capped at 10 (data-model.md) specifically to bound worst-case `react-rnd` instance/DOM cost per constitution §15's frontend performance concerns; `react-rnd` is a small, focused dependency, not a heavy one requiring lazy-loading like the spec 027 Google Maps loader. |

**Post-Phase-1 re-check**: Phase 0 (research.md) and Phase 1 (data-model.md, contracts/, quickstart.md)
are complete. No new violation surfaced during design — the registry pattern (Decision 1), the
`PanelHub`/`AgentExecutionHub` mirror (Decision 2), the `UserPanelPreference` SRP separation
(Decision 6), and the `ViewerEventBus` reuse (Decision 7) all reinforce the gates above rather than
straining them. Gate: **PASS**.

## Project Structure

### Documentation (this feature)

```text
specs/028-ai-floating-panels/
├── plan.md                       # This file (/speckit-plan command output)
├── research.md                   # Phase 0 output (/speckit-plan command)
├── data-model.md                 # Phase 1 output (/speckit-plan command)
├── quickstart.md                 # Phase 1 output (/speckit-plan command)
├── contracts/                    # Phase 1 output (/speckit-plan command)
│   ├── panel-hub-events.md
│   ├── panel-preferences-api.md
│   └── panel-type-registry.md
├── checklists/requirements.md
└── tasks.md                      # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Panels/
│       └── UserPanelPreference.cs            # Create/SetOpacityPercent, clamps [40,100]
├── AskLucy.Application/
│   ├── Abstractions/                          # flat folder (repo convention) — not per-feature
│   │   ├── IUserPanelPreferenceRepository.cs
│   │   └── IPanelNotifier.cs                  # PanelRequestedAsync(userId, PanelRequestDto)
│   └── Panels/
│       ├── UserPanelPreferenceDto.cs
│       ├── Queries/GetUserPanelPreference/
│       │   ├── GetUserPanelPreferenceQuery.cs
│       │   └── GetUserPanelPreferenceQueryHandler.cs
│       └── Commands/SaveUserPanelPreference/
│           ├── SaveUserPanelPreferenceCommand.cs
│           ├── SaveUserPanelPreferenceCommandHandler.cs
│           └── SaveUserPanelPreferenceCommandValidator.cs
├── AskLucy.Infrastructure/
│   └── Panels/
│       ├── PanelHub.cs                        # mirrors AgentExecutionHub (contracts/panel-hub-events.md)
│       └── PanelNotifier.cs                   # implements IPanelNotifier via IHubContext<PanelHub>
├── AskLucy.Persistence/
│   ├── DependencyInjection.cs                 # + repository registration (AddPersistence)
│   ├── Configurations/UserPanelPreferenceConfiguration.cs
│   ├── Repositories/UserPanelPreferenceRepository.cs
│   └── Migrations/..._AddUserPanelPreference.cs
└── AskLucy.Web/
    ├── Program.cs                             # + app.MapHub<PanelHub>("/hubs/panels")
    ├── Contracts/PanelsContracts.cs            # request/response DTOs (opacityPercent)
    ├── Controllers/v1/
    │   └── PanelsController.cs                # GET/PUT /api/v1/panels/preferences
    └── ClientApp/src/
        ├── viewer/                             # existing framework-agnostic viewer package (spec 027)
        │   └── panels/                         # NEW — the extensible panel framework
        │       ├── registry.ts                 # PanelTypeRegistry (contracts/panel-type-registry.md)
        │       ├── types/
        │       │   ├── index.ts                # imports every built-in type for registration side-effect
        │       │   ├── chart/ChartPanel.tsx
        │       │   ├── table/TablePanel.tsx
        │       │   ├── summary/SummaryPanel.tsx
        │       │   └── parameters/ParametersPanel.tsx
        │       ├── store/
        │       │   ├── floatingPanelStore.ts   # open panels, cascade placement, LRU eviction (data-model.md)
        │       │   └── panelPreferencesStore.ts # opacity, zustand+persist, mirrors voicePreferencesStore
        │       ├── hooks/
        │       │   └── useFloatingPanelHub.ts  # SignalR client (contracts/panel-hub-events.md)
        │       └── components/
        │           ├── FloatingPanel.tsx        # react-rnd chrome: drag/resize/minimize/close/focus
        │           └── FloatingPanelHost.tsx     # renders floatingPanelStore.panels, mounted in ViewerSurface
        └── features/
            ├── viewer/components/ViewerSurface.tsx  # MODIFIED — mounts <FloatingPanelHost />
            └── settings/
                ├── settingsTabs.ts                   # MODIFIED — append Viewer: 8
                ├── api/panelPreferencesApi.ts         # NEW
                └── pages/ViewerTab.tsx                # NEW — opacity slider

tests/
├── AskLucy.Application.Tests/Panels/
│   ├── GetUserPanelPreferenceQueryHandlerTests.cs
│   └── SaveUserPanelPreferenceCommandHandlerTests.cs
├── AskLucy.Domain.Tests/Panels/UserPanelPreferenceTests.cs
├── AskLucy.Infrastructure.Tests/Panels/PanelNotifierTests.cs
├── AskLucy.Web.Tests/Controllers/PanelsControllerTests.cs
├── AskLucy.E2E.Tests/AiFloatingPanels.spec.ts
└── (frontend) co-located *.test.tsx / *.a11y.test.tsx next to each new component under
    viewer/panels/ and features/settings/pages/ViewerTab.tsx, matching existing convention
```

**Structure Decision**: Existing single-solution web-application layout (Clean Architecture backend +
`ClientApp` React frontend) is reused as-is — no new project, no new solution folder. The panel
framework is a new `viewer/panels/` sub-package of the existing framework-agnostic `viewer/` package
(not `features/viewer/`), since panels are viewer-scoped infrastructure reusable by any future AI
agent/tool (spec's reusability requirement), mirroring how spec 027 kept the engine's own state
separate from that feature's page-level wiring. `features/viewer/ViewerSurface.tsx` gets one new
mount point (`<FloatingPanelHost />`) rather than owning any panel logic itself. The one new backend
aggregate (`Panels`) follows the same four-layer folder shape every other small preference feature in
this codebase already uses (`UserVoicePreference` as the direct precedent), so no new architectural
pattern is introduced.

## Complexity Tracking

*No entries — no Constitution Check gate was violated. The two new frontend dependencies
(`react-rnd`, `zod`) and the new `PanelHub`/`UserPanelPreference` backend surfaces are each grounded in
an existing in-repo pattern (research.md Decisions 2–6), not a deviation requiring justification.*
