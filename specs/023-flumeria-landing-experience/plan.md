# Implementation Plan: Flumeria Public Landing Experience

**Branch**: `023-flumeria-landing-experience` | **Date**: 2026-08-16 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-flumeria-landing-experience/spec.md`

## Summary

Add a public marketing landing page at the application's root URL that introduces Flumeria (an AI Urban Design Platform) with Ask Lucy positioned as its underlying AI capability, restyle every auth-flow page (sign-in, sign-up, email confirmation, email-change confirmation, external-login completion) to match the landing page's visual language, and route both flows into the existing, unmodified authenticated workspace. The technical approach reuses the existing React 19 / MUI / React Router SPA and its existing `AuthLayout` shell rather than building new infrastructure, reuses the existing three.js/`@react-three/fiber` stack already used for the chat scene for hero visuals (no new visualization dependency), and adds two small, well-contained backend/frontend additions: (1) an anonymous-safe consent mechanism for the new public pages (the existing `CookieConsentController`/`ConsentGate` is authenticated-only and cannot be reused as-is), and (2) a lightweight, consent-gated funnel/CTA analytics event endpoint recorded via structured logging rather than a new database table. No changes to authentication contracts, the authenticated workspace's layout, or any existing Domain entity.

## Technical Context

**Language/Version**: TypeScript ~6.0.2 (frontend, `src/AskLucy.Web/ClientApp`); C# / .NET 10 (backend, `src/AskLucy.*`) — both unchanged from current versions in use.

**Primary Dependencies**: React 19.2, React Router 8.3, MUI 9.2, Zustand 5.0, TanStack Query 5.101, React Hook Form 7.83, Vite 8.1, three.js + `@react-three/fiber`/`@react-three/drei`/`@react-three/postprocessing` (all pre-existing, reused for landing hero visuals — see research.md). Backend: ASP.NET Core, MediatR, FluentValidation, EF Core, Serilog (pre-existing). **No new runtime package is introduced by this feature** (see research.md for the SEO-metadata and analytics-vendor decisions that avoid new dependencies).

**Storage**: SQL Server (existing). This feature adds **no new table and no EF Core migration** — funnel/CTA analytics events are recorded as structured Serilog log entries, not persisted rows (research.md Topic 4); the new anonymous public-consent decision is stored client-side only (a first-party cookie), not server-side (research.md Topic 2).

**Testing**: vitest + `@testing-library/react` + `jest-axe` (frontend unit/component/accessibility, existing), Playwright (frontend E2E, `tests/AskLucy.E2E.Tests`, existing), xUnit v3 (backend unit/integration, `tests/AskLucy.{Domain,Application,Infrastructure,Persistence,Web}.Tests`, existing).

**Target Platform**: Web — existing ASP.NET Core host serving the Vite-built SPA. No new deployment target.

**Project Type**: Web application (frontend + backend in one layered solution) — existing structure; no new projects.

**Performance Goals**: Landing page CTAs and above-the-fold content must be interactive without waiting on heavy visuals; any hero visual built on the three.js stack is code-split behind the landing route bundle (existing `<Lazy>`/`React.lazy` pattern), consistent with constitution §15's mandatory route-level code splitting and lazy-loading of large dependencies.

**Constraints**: No new external analytics vendor (constitution "avoid unnecessary dependencies" + provider-neutral ethos — see research.md Topic 4). No change to authentication contracts, token issuance, or the authenticated workspace's layout/functionality (spec Assumptions). The existing `CookieConsentController` stays `[Authorize]`-only and untouched — this feature does not modify specs/004's Domain entity or validation rules.

**Scale/Scope**: One new public route (`/`), visual restyle of 5 existing auth-flow routes (no route-path changes), one new anonymous-allowed analytics endpoint, one new anonymous-safe public consent banner/mechanism, and a minimal addition to the chat workspace's existing top bar. No new persisted Domain entities.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design (see "Post-Design Re-check" below).*

| Principle / Section | Assessment |
|---|---|
| **I. Clean Architecture & Dependency Rule** | PASS. New backend work is one MediatR command (`Application/Analytics/Commands/RecordFunnelEvent`) invoked from a thin `AnalyticsController` (`Api`); no Domain changes; no layer references an outward layer. |
| **II. SOLID** | PASS. `RecordFunnelEventCommand`/Handler has one reason to change (record a funnel event). No new interfaces needed beyond the existing `ILogger<T>` abstraction (see V below) — avoids an unnecessary single-purpose wrapper interface. |
| **III. Simplicity — DRY/KISS/YAGNI** | PASS, and drives two explicit decisions: (a) analytics events are logged, not persisted to a new table (avoids a migration and a CRUD surface for what is fundamentally telemetry, not business data); (b) SEO metadata uses React 19's native `<title>`/`<meta>` hoisting instead of adding `react-helmet-async` (research.md Topic 1). |
| **IV. Composition Over Inheritance** | PASS — N/A; no new class hierarchies. |
| **V. Dependency Inversion & Testability** | PASS. The new Application handler depends on `Microsoft.Extensions.Logging.ILogger<T>` directly, the same pattern already used by existing Application-layer code (e.g. `Application/Workflows/EventTriggers/WorkflowEventTriggerHandler.cs`, `Application/Memory/MemoryService.cs`) — not a new Infrastructure dependency, and unit-testable via a fake/mock logger with no database or network access. |
| **VI. Separation of Concerns** | PASS. `AnalyticsController` contains no logic beyond mapping the request to the command and dispatching via `ISender`; validation lives in a FluentValidation validator, not the controller. |
| **VII. Convention Over Configuration** | PASS. New endpoint follows the existing versioned-controller, MediatR, and per-endpoint fixed-window rate-limiting patterns already established in `Program.cs` for other public endpoints — no bespoke mechanism introduced. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | PASS, with one explicit scoping note: funnel/CTA analytics emission (FR-021) is intentionally fire-and-forget on the frontend — a failed analytics call is caught, logged at `warn` level client-side, and does **not** block, delay, or surface user-facing error UI for the CTA's actual navigation. This does not violate Principle VIII: the principle targets failures that affect a user's ability to complete their task or leave them confused about an outcome; here the user's actual task (navigating via a CTA) always succeeds independent of telemetry delivery, so there is no "nothing happened" failure mode for the user. Real user-facing operations in this feature — sign-in, sign-up, the other auth-flow pages — keep their existing, already-compliant error handling (FR-017) unchanged. |
| **§5 Database Principles** | N/A by design — no new entity, index, or migration (justified above under III). |
| **§6 API Standards** | PASS. New endpoint is versioned (`/api/v1/analytics/funnel-events`), returns Problem Details on validation failure, is rate-limited, and its anonymous access is an explicit, reviewed opt-in (`[AllowAnonymous]`) as required by §6 AuthN/AuthZ — documented in contracts/analytics-funnel-events-api.md. |
| **§7 UI Principles** | PASS. Landing/auth pages compose from the existing MUI theme and extend the existing `AuthLayout` component rather than building a bespoke one (design-system-first, §7); both light/dark themes and full responsiveness are required (FR-012/FR-013); no dashboard-style nav is introduced (FR-014); voice-persona and other unrelated UI principles are untouched (out of scope). |
| **§8 Security** | PASS, with an explicit threat-model note: the new anonymous endpoint accepts no PII, restricts `eventType`/`ctaId`/`funnelType` to a closed enum (FluentValidation), is rate-limited per-IP (mirroring existing anonymous endpoint policies in `Program.cs`), and treats the client's consent assertion as a trust boundary standard for anonymous telemetry (equivalent to how any pre-auth analytics beacon works) — it does not gate access to any sensitive data or capability. |
| **§9 AI Principles** | N/A — this feature adds no AI/LLM calls. |
| **§10 Testing Standards** | PASS — planned coverage: Application unit tests for the command/validator (mocked logger), Web integration test for the controller (rate limit + validation + anonymous access), frontend component/a11y tests for the landing and restyled auth pages, and one new Playwright E2E scenario extending the existing critical-journey suite (landing → sign-up → workspace). |
| **§14 Observability** | PASS, and reinforced — this feature's telemetry approach (structured Serilog events with named properties) is a direct instance of the existing Observability architecture rather than a parallel one. |
| **§15 Performance** | PASS, with a verification task added (T057a). Route-level code splitting already applies to `LandingHero`'s three.js bundle (research.md Topic 3); this is measured, not just assumed, per the added task below. |

No unresolved violations. **Complexity Tracking table is empty** — no principle is being bent to satisfy this feature.

**Note on FR-020 wording**: the spec's clarification session settled on "wrap the public pages with the existing consent gate." Planning discovered that the literal existing `ConsentGate`/`CookieConsentController` is authenticated-only (`[Authorize]`, keyed by `UserId`) and cannot render for a signed-out visitor without breaking (a 401 would surface as ConsentGate's full-page error state). research.md Topic 2 documents the resolution: a new, visually-consistent, anonymous-safe companion mechanism (`PublicConsentGate`) that reuses the same category taxonomy and banner visual language as the authenticated flow, satisfying FR-020's intent (consent obtained before any tracking, consistent UX) without modifying specs/004's authenticated Domain model. This is called out explicitly here, in research.md, and in the completion report so it isn't silently buried.

## Project Structure

### Documentation (this feature)

```text
specs/023-flumeria-landing-experience/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md         # Phase 1 output
├── quickstart.md         # Phase 1 output
├── contracts/            # Phase 1 output
│   ├── analytics-funnel-events-api.md
│   └── routing-and-consent-contract.md
└── tasks.md              # Phase 2 output (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/
├── features/
│   ├── landing/                                    # NEW
│   │   ├── pages/LandingPage.tsx
│   │   ├── components/
│   │   │   ├── LandingHero.tsx                     # reuses existing three.js scene primitives
│   │   │   ├── ValuePropositionSection.tsx
│   │   │   ├── HowItWorksSection.tsx                # Lucy + AI-assisted urban design narrative
│   │   │   ├── SpatialIntegrationSection.tsx        # GIS/maps/2D-3D/spatial-analysis narrative
│   │   │   ├── AnalysisVisualizationSection.tsx     # how AI-generated analysis is visualized
│   │   │   ├── DifferentiationSection.tsx
│   │   │   └── LandingCtaBar.tsx                    # the 3 primary CTAs
│   │   └── content/copy.ts                          # centralized landing copy (i18n-ready per §7)
│   ├── consent/
│   │   ├── components/ConsentGate.tsx                # UNCHANGED (authenticated flow)
│   │   ├── components/PublicConsentGate.tsx           # NEW — anonymous-safe companion
│   │   ├── components/PublicConsentBanner.tsx         # NEW — reuses cookieCategories.ts taxonomy
│   │   ├── hooks/usePublicCookieConsent.ts             # NEW — client-cookie-backed, no API call
│   │   └── cookieCategories.ts                        # UNCHANGED, reused for label consistency
│   ├── analytics/                                      # NEW
│   │   ├── hooks/useFunnelAnalytics.ts                 # fire-and-forget, consent-gated
│   │   └── api/analyticsApi.ts
│   └── auth/pages/
│       ├── LoginPage.tsx                               # visual restyle only
│       ├── RegisterPage.tsx                            # visual restyle only
│       ├── ConfirmEmailPage.tsx                        # visual restyle only
│       ├── ConfirmEmailChangePage.tsx                  # visual restyle only
│       └── ExternalLoginCompletePage.tsx               # visual restyle only
├── components/
│   ├── AuthLayout.tsx                                  # EXTENDED (Flumeria brand copy), not replaced
│   └── PublicOnlyRoute.tsx                             # NEW — inverse of ProtectedRoute
├── routes/router.tsx                                    # MODIFIED — new "/" route, PublicOnlyRoute wrapper
└── features/chat/components/MinimalTopBar.tsx            # MODIFIED — minimal brand-transition element (FR-011)

src/AskLucy.Web/
├── Controllers/v1/AnalyticsController.cs                 # NEW — POST /api/v1/analytics/funnel-events
└── Program.cs                                            # MODIFIED — new named rate-limit policy

src/AskLucy.Application/
└── Analytics/Commands/RecordFunnelEvent/
    ├── RecordFunnelEventCommand.cs                        # NEW
    ├── RecordFunnelEventCommandHandler.cs                 # NEW
    └── RecordFunnelEventCommandValidator.cs                # NEW

tests/
├── AskLucy.Application.Tests/Analytics/RecordFunnelEventCommandHandlerTests.cs   # NEW
├── AskLucy.Web.Tests/Controllers/AnalyticsControllerTests.cs                     # NEW
├── AskLucy.E2E.Tests/ (new spec: landing-to-workspace journey)                   # NEW
└── AskLucy.Web/ClientApp/src/features/landing/**/*.test.tsx (+ a11y)             # NEW
```

**Structure Decision**: Existing single-solution web application layout (`src/AskLucy.{Domain,Application,Infrastructure,Persistence,Web}` backend + `src/AskLucy.Web/ClientApp` frontend SPA) is reused as-is. No new top-level project. All additions are new files within existing feature-folder conventions (`features/<domain>` on the frontend, `Application/<Domain>/Commands/<Name>` on the backend) — no parallel structure introduced.

## Complexity Tracking

*No entries — Constitution Check above found no violations requiring justification.*
