# Implementation Plan: Presence Sphere Admin Controls

**Branch**: `080-presence-sphere-admin-controls` | **Date**: 2026-10-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/080-presence-sphere-admin-controls/spec.md`

## Summary

Move the three presence-sphere settings that keep being tuned in code (dot size, sphere-to-card fill, zoom on/off) into one workspace-wide settings row that administrators edit on a new Admin "Appearance" page with a live preview. Every signed-in user's chat reads the row and applies it. Approach: a single-row table following the specs/078 `DictationEngineSetting` precedent, a MediatR query/command pair, two new admin permissions, one read endpoint for any signed-in user and one admin-only write endpoint, and a small frontend settings hook that falls back to today's constants when the read fails.

## Technical Context

**Language/Version**: C# / .NET 10; TypeScript / React 19

**Primary Dependencies**: MediatR, FluentValidation, EF Core (SQL Server), Serilog; React, MUI, TanStack Query, React Hook Form + Zod, three.js via @react-three/fiber and drei (all existing, nothing new)

**Storage**: SQL Server, one new single-row table `PresenceSphereSettings` (code-first migration)

**Testing**: xUnit (Domain, Application, Web integration), Vitest + Testing Library (+ jest-axe for the new page, like the other admin pages)

**Target Platform**: Web (desktop and touch browsers); admin page desktop-first

**Project Type**: Web application (ASP.NET Core API + React SPA, Clean Architecture)

**Performance Goals**: preview updates within 500 ms of a control change (SC-001); the chat's first paint must not wait on the settings read (SC-007)

**Constraints**: 1.0x / 75% / zoom off must be pixel-identical to today when nothing is saved (FR-018); no deployment to change values (FR-009); no new package

**Scale/Scope**: one row, read once per page load per user; one new admin page; about 3 files touched in the sphere scene

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design: still passes.*

| Principle | Assessment |
|---|---|
| I. Clean Architecture | Entity in Domain, handlers and a repository abstraction in Application, EF configuration and repository in Persistence, thin controller in Web. Dependencies point inward. **Pass** |
| III. Simplicity / YAGNI | One row, three values, no per-user override, no live push, no new general audit system. No optimistic-concurrency check because the spec chooses last-write-wins. **Pass** |
| V. Dependency inversion | Handlers depend on `IPresenceSphereSettingsRepository` defined in Application. **Pass** |
| VI. Separation of concerns | Range validation lives in the Domain and FluentValidation, not in the controller or the React page. The page and the scene only render. **Pass** |
| VII. Convention over configuration | Follows the 078 singleton, the `RequirePermission` attribute, and the existing admin page, nav and permission-catalogue conventions. **Pass** |
| VIII. No silent failures | Settings-read failure: the sphere falls back to defaults and the failure is captured and logged. Save failure: visible error, values kept. Query and mutation both have explicit error paths. **Pass** (research D6) |
| Section 6 API standards | Versioned route, Problem Details on 400/403, DTOs not entities. **Pass** |
| Section 8 Security | Write needs `admin.appearance.manage`; read needs authentication only; inputs validated server-side. **Pass** |
| Section 5 Database | Code-first migration; must be applied by hand to the shared persistence test DB (known repo gotcha). **Pass, with a task** |
| Section 10 Testing | Unit, integration and component tests listed in the quickstart. **Pass** |

No violations; Complexity Tracking is not needed.

## Project Structure

### Documentation (this feature)

```text
specs/080-presence-sphere-admin-controls/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── presence-sphere-api.md
└── tasks.md             # created by /speckit-tasks
```

### Source Code (repository root)

```text
src/AskLucy.Domain/
├── Appearance/PresenceSphereSettings.cs            # single-row entity, ranges, defaults, Update()
└── Authorization/
    ├── AdminArea.cs                                # + Appearance
    └── AdminPermissionCatalog.cs                   # + admin.appearance.view / .manage (23 -> 25)

src/AskLucy.Application/
├── Abstractions/IPresenceSphereSettingsRepository.cs
└── Appearance/
    ├── PresenceSphereSettingsDto.cs
    ├── Queries/GetPresenceSphereSettings/          # query + handler (stored values or defaults)
    └── Commands/UpdatePresenceSphereSettings/      # command + validator + handler (+ log event)

src/AskLucy.Persistence/
├── Configurations/PresenceSphereSettingsConfiguration.cs
├── Repositories/PresenceSphereSettingsRepository.cs
├── AskLucyDbContext.cs, DependencyInjection.cs     # DbSet + registration
└── Migrations/<timestamp>_AddPresenceSphereSettings.cs

src/AskLucy.Web/
└── Controllers/v1/AppearanceController.cs          # GET (authenticated), PUT (RequirePermission manage)

src/AskLucy.Web/ClientApp/src/
├── features/admin/
│   ├── adminPermissions.ts, adminNav.tsx           # + permissions, + "Appearance" entry and route
│   ├── api/adminAppearanceApi.ts
│   └── pages/AdminAppearancePage.tsx (+ test, a11y test)   # sliders, switch, reset/discard, live preview
└── features/chat/
    ├── scene/sphereConstants.ts                    # defaults and clamp ranges (one source for both sides)
    ├── scene/SceneBackground.tsx, ReactiveSphere.tsx   # take settings as props instead of module constants
    ├── components/AiPresenceCard.tsx               # reads settings, passes them down
    └── hooks/usePresenceSphereSettings.ts          # query with defaults fallback and error capture

tests/
├── AskLucy.Domain.Tests/            # entity ranges; permission catalogue counts (23 -> 25)
├── AskLucy.Application.Tests/       # query defaults, command validation, logging
└── AskLucy.Web.Tests/               # auth matrix (anon/user/view-only/manage), 400 on out-of-range
```

**Structure Decision**: The existing Clean Architecture web-app layout. A new `Appearance` feature folder in Domain and Application mirrors how other features (for example `Ai/Dictation`) are laid out. The admin page stays under `features/admin`; the sphere changes stay under `features/chat/scene`.

## Complexity Tracking

None.
