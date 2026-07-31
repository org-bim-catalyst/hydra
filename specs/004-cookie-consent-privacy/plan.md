# Implementation Plan: Cookie Consent & Privacy Management

**Branch**: `004-cookie-consent-privacy` | **Date**: 2026-07-30 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-cookie-consent-privacy/spec.md`

## Summary

Add a strict-opt-in cookie consent system: a blocking banner shown on the main app page
until an authenticated user records an explicit decision (Accept All / Reject
Non-Essential / Customize per category), a public Privacy Page, and a Cookie Preferences
panel inside Settings. Implemented as a new, narrowly-scoped "Consent" concern — one
append-only `CookieConsentRecord` per decision (current state = latest row per user),
exposed via three endpoints on a new `CookieConsentController`, and a new `features/consent`
frontend feature that gates the existing protected app shell. No new datastore, no new
frontend dependency — reuses the project's existing MediatR/EF Core/TanStack Query/MUI
stack (research.md).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 (frontend)

**Primary Dependencies**: ASP.NET Core, EF Core, MediatR, FluentValidation, Serilog
(backend, all existing — no new package); React 19, Vite, Material UI, TanStack Query,
Zustand, React Hook Form (frontend, all existing — no new package; the blocking banner
uses MUI `Dialog`, already the project's modal primitive). Note: CLAUDE.md's target stack
also names Zod and Axios, but neither is actually present in `package.json` or imported
anywhere in `ClientApp/src` — data fetching goes through a native-`fetch`-based `apiFetch`
wrapper (`api/httpClient.ts`), confirmed during implementation; this feature follows that
actual convention, not the aspirational one.

**Storage**: SQL Server via EF Core Code-First migrations (existing `AskLucyDbContext`);
one new append-only table, `CookieConsentRecords` (research.md Topic 2).

**Testing**: xUnit (`AskLucy.Domain.Tests`, `AskLucy.Application.Tests`,
`AskLucy.Persistence.Tests`, `AskLucy.Web.Tests`), Playwright (`AskLucy.E2E.Tests`) —
existing test projects, extended with this feature's cases. `AskLucy.Infrastructure.Tests`
gains one test (the new `ICookiePolicyProvider` implementation).

**Target Platform**: ASP.NET Core web API + server-hosted SPA (`AskLucy.Web` hosts both
the API and the built `ClientApp`), existing deployment targets unchanged.

**Project Type**: Web application (backend + frontend within one solution/repo).

**Performance Goals**: Consent-status lookup adds one indexed, single-row query
("latest `CookieConsentRecord` for this user") to the authenticated app's initial load;
negligible added latency (no stated numeric SC beyond "immediate," per spec.md SC-004).

**Constraints**: No new datastore (constitution §5 — reuses SQL Server); consent/privacy
copy is English-only at initial launch, centralized rather than hardcoded per component
(spec.md FR-021); the banner MUST block interaction with the rest of the app until an
explicit decision is recorded (spec.md FR-020); zero Functional/Analytics/Marketing
activity may occur for any user, in any location, before that decision (strict global
opt-in, spec.md FR-019).

**Scale/Scope**: 3 user stories (P1×1, P2×1, P3×1); 1 new Domain entity
(`CookieConsentRecord`, append-only, no update/delete methods); 1 new controller with 3
actions (2 authenticated, 1 anonymous); 1 new frontend feature folder (`features/consent`)
plus 1 new public-page feature folder (`features/privacy`); 1 new shared component
(`AppFooter` — the project currently has no global footer, research.md); 1 existing page
extended (`SettingsPage.tsx` gains a fourth tab).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design below.*

| Principle / Rule | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | PASS | New `CookieConsentRecord` lives in `Domain/Consent/`; `Application/Consent/` depends only on Domain + its own `Abstractions` interfaces; `Persistence`/`Infrastructure` implement those interfaces; `Web` composes via MediatR only — mirrors the existing `Chats`/`Users` layering exactly. |
| II. SOLID | PASS | `SaveMyCookieConsentCommand` and `GetMyCookieConsentQuery` are separate single-purpose handlers (SRP); `ICookiePolicyProvider`/`IUserCookieConsentRepository` are narrow, single-client interfaces (ISP), not folded into existing `IUserProfileRepository`. |
| III. Simplicity — DRY/KISS/YAGNI | PASS | One append-only table serves both "current state" (latest row) and "audit history" (FR-016) — no separate audit-trail table invented (research.md Topic 2). Cookie category metadata (name/description) is a small, stable, frontend-only constant, not round-tripped through an API (research.md Topic 4) — avoids a speculative admin-editable-categories feature nothing in spec.md asks for. |
| IV. Composition over inheritance | PASS | `CookieConsentRecord` is a plain `BaseEntity` child, no inheritance introduced. |
| V. Dependency Inversion & Testability | PASS | Handlers depend on `IUserCookieConsentRepository`/`ICookiePolicyProvider`/`ICurrentUserAccessor` (Application-owned interfaces); fully unit-testable with fakes, no DB required (mirrors existing `GetMyProfileQueryHandler` pattern, research.md §2). |
| VI. Separation of Concerns | PASS | `CookieConsentController` only calls `mediator.Send(...)`; reconsent-required logic (comparing stored vs. current policy version) lives in the query handler, not the controller. |
| VII. Convention Over Configuration | PASS | Reuses the established `Application/<Feature>/Commands|Queries/<VerbNoun>` folder shape, the `IEntityTypeConfiguration<T>` + `ApplyConfigurationsFromAssembly` persistence convention, the `[Authorize]`-by-default + per-action `[AllowAnonymous]` controller convention (`UsersController.GetAvatar` precedent), and the existing named-rate-limit-policy pattern in `Program.cs` — no parallel mechanism introduced anywhere. |
| VIII. No Silent Failures | PASS | Consent load/save failures surface via TanStack Query's `isError`/`onError` state to a visible `Alert`/toast (research.md §5, matching `SettingsPage.tsx`'s existing `changePassword.isError` idiom); backend exceptions bubble to the existing `ProblemDetailsMiddleware`, never caught-and-discarded. |
| §3 Architecture Rules (CQRS, Repository/UoW, Infrastructure isolation) | PASS | Every action is a MediatR command/query; the consent save is a single `SaveChangesAsync` call (one row insert, no multi-step transaction needed); `ICookiePolicyProvider` hides `IOptions<CookiePolicyOptions>` behind an Application-owned interface, implemented in `Infrastructure`, never referenced directly from Application. |
| §5 Database Principles (soft delete, indexing, no new datastore) | PASS | No new datastore; `(UserId, CreatedAtUtc DESC)` index added in the same migration that needs it (research.md Topic 2); `CookieConsentRecord` inherits `BaseEntity`'s existing soft-delete/audit columns for GDPR-erasure consistency, even though normal application flow never updates or deletes a row (append-only). |
| §6 API Standards (REST, Problem Details, versioning, rate limiting) | PASS | `/api/v1/users/me/cookie-consent` (GET/PUT) + `/api/v1/cookie-policy` (GET, anonymous) — versioned, Problem-Details error shape by default; new `consent-endpoints` rate-limit policy added following the exact `admin-endpoints`/`chat-endpoints` precedent in `Program.cs`, partitioned by user name or remote IP for the anonymous policy endpoint (research.md §4). |
| §7 UI Principles (design system, accessibility, theming, state mgmt) | PASS | Banner built from MUI `Dialog` (existing modal primitive, no bespoke component); `AppFooter` is a new shared component justified by ≥2 usage sites (`AuthLayout` + `PrivacyPage`, §7's "used by at least two features" bar) — the authenticated app shell (`ChatPage`) has no footer region, so that leg of FR-010's reachability is instead covered by a new `UserMenu` item (research.md Topic 9, revised during implementation); server state (consent status) lives in TanStack Query, not duplicated into Zustand (research.md §5). |
| §8 Security | PASS | Both "me" endpoints resolve the acting user via `ICurrentUserAccessor`, never a client-supplied userId (mirrors `GetMyProfileQueryHandler`); consent changes are logged via structured Serilog (interim security-event logging, matching the SPEC-001-documented precedent — no project-wide audit-trail store exists yet, tracked separately in `docs/adr/`). |
| §10 Testing Standards | PASS | Plan allocates unit tests (Domain entity + Application handlers), integration tests (Persistence repository/index), controller tests (Web, auth/anonymous wiring), and Playwright E2E per user story (tasks.md, next phase). |

No violations requiring justification — **Complexity Tracking is empty** (see below).

**Post-Phase-1 re-check**: Re-validated after Phase 1 design (research.md, data-model.md,
contracts/cookie-consent-api.md) was written. Every row above cites the specific decision
that grounds it (append-only table instead of a parallel audit store, frontend-only
category metadata instead of a categories API, `ICookiePolicyProvider` abstraction instead
of a direct `IOptions<T>` dependency in Application). No gate regressed once the concrete
data model and API shape were fixed; Complexity Tracking remains empty.

## Project Structure

### Documentation (this feature)

```text
specs/004-cookie-consent-privacy/
├── plan.md                    # This file (/speckit-plan command output)
├── research.md                # Phase 0 output (/speckit-plan command)
├── data-model.md               # Phase 1 output (/speckit-plan command)
├── quickstart.md               # Phase 1 output (/speckit-plan command)
├── contracts/                  # Phase 1 output (/speckit-plan command)
│   └── cookie-consent-api.md
├── checklists/
│   └── requirements.md
└── tasks.md                    # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Consent/
│       └── CookieConsentRecord.cs        # new: BaseEntity, append-only (no Update/Delete methods)
├── AskLucy.Application/
│   ├── Abstractions/
│   │   ├── IUserCookieConsentRepository.cs   # new
│   │   └── ICookiePolicyProvider.cs          # new
│   └── Consent/
│       ├── CookieConsentStatusDto.cs         # new
│       ├── CookiePolicyDto.cs                # new
│       ├── Commands/
│       │   └── SaveMyCookieConsent/
│       │       ├── SaveMyCookieConsentCommand.cs
│       │       ├── SaveMyCookieConsentCommandHandler.cs
│       │       └── SaveMyCookieConsentCommandValidator.cs
│       └── Queries/
│           ├── GetMyCookieConsent/
│           │   ├── GetMyCookieConsentQuery.cs
│           │   └── GetMyCookieConsentQueryHandler.cs
│           └── GetCookiePolicy/
│               ├── GetCookiePolicyQuery.cs
│               └── GetCookiePolicyQueryHandler.cs
├── AskLucy.Infrastructure/
│   ├── Consent/
│   │   ├── CookiePolicyOptions.cs            # new: bound via IOptions<T>, validated at startup
│   │   └── CookiePolicyProvider.cs           # new: ICookiePolicyProvider impl reading IOptions<CookiePolicyOptions>
│   └── DependencyInjection.cs                # extended: binds CookiePolicyOptions, registers ICookiePolicyProvider
├── AskLucy.Persistence/
│   ├── Configurations/
│   │   └── CookieConsentRecordConfiguration.cs  # new: (UserId, CreatedAtUtc DESC) index + cascade-delete FK to ApplicationUser
│   ├── Repositories/
│   │   └── UserCookieConsentRepository.cs       # new
│   ├── Identity/ApplicationUser.cs              # extended: CookieConsentRecords navigation property (mirrors UserChats)
│   ├── AskLucyDbContext.cs                      # extended: DbSet<CookieConsentRecord>
│   ├── DependencyInjection.cs                    # extended: registers IUserCookieConsentRepository
│   └── Migrations/
│       └── 20260730105021_AddCookieConsent.cs   # new
└── AskLucy.Web/
    ├── Controllers/v1/
    │   └── CookieConsentController.cs        # new: [Authorize] class-level, [AllowAnonymous] on the policy action
    ├── Contracts/
    │   └── CookieConsentContracts.cs         # new
    ├── Middleware/ProblemDetailsMiddleware.cs # fixed pre-existing bug: ContentType was silently reset to
    │                                          # application/json by WriteAsJsonAsync; now passed explicitly
    ├── Program.cs                            # extended: "consent-endpoints" rate-limit policy only
    │                                          # (options binding/repo registration live in the Add*() extension
    │                                          # methods above, per this codebase's actual convention)
    ├── appsettings.json                       # extended: CookiePolicy:CurrentVersion/EffectiveAtUtc section
    └── ClientApp/src/
        ├── components/
        │   ├── AppFooter.tsx                 # new: shared footer, Privacy link (AuthLayout + PrivacyPage)
        │   ├── AuthLayout.tsx                 # extended: renders AppFooter
        │   └── UserMenu.tsx                   # extended: "Privacy Policy" MenuItem (authenticated global-nav leg of FR-010)
        ├── routes/
        │   ├── router.tsx                    # extended: public /privacy route; ProtectedRoute wraps content in ConsentGate
        │   └── ProtectedRoute.tsx             # extended: renders <ConsentGate> around its children
        └── features/
            ├── consent/
            │   ├── api/consentApi.ts          # new
            │   ├── hooks/useCookieConsent.ts  # new: query + mutation
            │   ├── cookieCategories.ts        # new: shared category name/description constant (banner + panel + privacy page)
            │   └── components/
            │       ├── ConsentGate.tsx        # new: fetches status, renders banner as blocking overlay when required
            │       ├── CookieConsentBanner.tsx # new: MUI Dialog, non-dismissible, Accept All/Reject/Customize
            │       ├── CookieConsentBanner.a11y.test.tsx      # new
            │       ├── CookiePreferencesPanel.tsx # new: rendered as SettingsPage's 4th tab
            │       └── CookiePreferencesPanel.a11y.test.tsx   # new
            ├── privacy/
            │   ├── api/privacyApi.ts          # new: getCookiePolicy()
            │   ├── hooks/useCookiePolicy.ts   # new
            │   └── pages/
            │       ├── PrivacyPage.tsx        # new: public page, English-only static content + policy version/date
            │       └── PrivacyPage.a11y.test.tsx  # new
            └── settings/pages/SettingsPage.tsx # extended: adds "Cookies" Tab rendering CookiePreferencesPanel

tests/
├── AskLucy.Domain.Tests/Consent/CookieConsentRecordTests.cs
├── AskLucy.Application.Tests/Consent/
│   ├── SaveMyCookieConsentCommandHandlerTests.cs
│   ├── GetMyCookieConsentQueryHandlerTests.cs
│   └── GetCookiePolicyQueryHandlerTests.cs
├── AskLucy.Infrastructure.Tests/Consent/CookiePolicyProviderTests.cs
├── AskLucy.Persistence.Tests/Consent/UserCookieConsentRepositoryTests.cs
├── AskLucy.Web.Tests/
│   ├── Consent/
│   │   ├── CookieConsentControllerTests.cs
│   │   └── CookiePolicyEndpointTests.cs
│   └── CustomWebApplicationFactory.cs         # extended: seeds CookiePolicy:CurrentVersion/EffectiveAtUtc
│                                               # in-memory config, otherwise this feature's ValidateOnStart()
│                                               # would break every existing test using this factory
└── AskLucy.E2E.Tests/
    ├── CookieConsentBanner.spec.ts
    ├── CookiePreferencesSettings.spec.ts
    └── PrivacyPage.spec.ts

docs/ARCHITECTURE.md                            # extended: new §26 "Consent & Privacy Engine"
```

**Structure Decision**: Web application (Option 2), realized as the existing single
`AskLucy.Web` project (API + built `ClientApp`) plus `Domain`/`Application`/
`Infrastructure`/`Persistence` — this feature adds a new `Consent` feature folder to each
backend project (following the same per-feature folder convention `Chats`/`Users` already
use) and two new frontend feature folders (`features/consent`, `features/privacy`), plus
one new shared component (`AppFooter`) and one extended existing page (`SettingsPage`),
rather than introducing new top-level projects. A small number of pre-existing files
required fixes/extensions discovered only during implementation (`ProblemDetailsMiddleware`
bug fix, `CustomWebApplicationFactory` config, `AuthLayout`/`UserMenu` for footer/nav
placement) — see research.md Topic 9 and tasks.md's per-task notes for the reasoning
behind each.

## Complexity Tracking

*No entries — Constitution Check reported no violations requiring justification.*
