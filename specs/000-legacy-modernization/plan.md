# Implementation Plan: Legacy Application Modernization & Technology Stack Migration

**Branch**: `000-legacy-modernization` | **Date**: 2026-07-27 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/000-legacy-modernization/spec.md`

## Summary

Migrate the existing "Ask Lucy" application (a single, untested, .NET 7 ASP.NET Core MVC/Razor project with no layering, cookie-based auth, and four unauthenticated OpenAI proxy endpoints) onto the Clean Architecture / CQRS / JWT technology stack approved in `.specify/memory/constitution.md` and `docs/*.md`, without changing any existing user-facing capability except the three explicitly approved deviations captured in the spec's Clarifications (anonymous-AI-access closure, chat rename/delete completion, and the deferred hosting/credential decisions). The approach is an incremental, six-step, module-by-module migration (Foundation → Authentication → AI endpoints → Data → Frontend → Decommission) that keeps the legacy project deployable throughout, per `spec.md` § Migration Strategy.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript 5.x / React 19 (frontend)

**Primary Dependencies**: ASP.NET Core (.NET 10), MediatR (CQRS), FluentValidation, AutoMapper, ASP.NET Core Identity, JWT Bearer authentication, Serilog, Swashbuckle (OpenAPI) — backend. React 19, Vite, MUI, React Router, TanStack Query, Zustand, React Hook Form — frontend.

**Storage**: SQL Server via EF Core Code-First (existing production database, migrated in place); local filesystem for the user avatar file, served via a signed download URL (per `docs/ARCHITECTURE.md` §17 `IFileStorage`/`LocalFileStorage`).

**Testing**: xUnit + FluentAssertions + NSubstitute + ASP.NET Core `WebApplicationFactory` + Testcontainers for SQL Server (backend, per `docs/TESTING.md` §6–13); Vitest + React Testing Library + MSW (frontend); Playwright (end-to-end regression matrix).

**Target Platform**: Existing `site4now.net` shared IIS/out-of-process hosting, deployed via GitHub Actions to the existing publish mechanism. Docker and Azure App Service are explicitly **not** targeted this phase (approved deviation, see § Constitution Check).

**Project Type**: Web application (ASP.NET Core Web API backend + React SPA frontend), replacing a server-rendered MVC/Razor monolith.

**Performance Goals**: SC-006 — P95 time-to-first-visible-content under 2 seconds for a standard chat message under normal load (streamed via SSE).

**Constraints**: Zero data loss for existing accounts/chats (SC-009); zero change to any preserved capability (FR-001–FR-014, FR-033); AI endpoints must require auth and be rate-limited (FR-015, FR-023) with role-tiered thresholds; must not introduce Docker or an Azure hosting cutover (FR-030); must not introduce persisted message history, RAG, agents, or any other Phase 1+ capability (FR-026).

**Scale/Scope**: Fewer than 100 total registered users, low concurrency (per spec Clarifications) — sized as a single-instance deployment, not a multi-tenant/high-scale system.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Constitution area | Status | Notes |
|---|---|---|
| I. Clean Architecture & Dependency Rule | PASS | New `Domain`/`Application`/`Infrastructure`/`Persistence`/`WebAPI` projects with dependencies pointing inward only (§ Project Structure). |
| II. SOLID | PASS | CQRS handlers per use case; `IAIProvider`/`IFileStorage`/repository interfaces own by Application/Domain. |
| III. Simplicity — DRY/KISS/YAGNI | PASS | No RAG/agents/multi-provider/billing scaffolding introduced (FR-026); only the entities needed for FR-001–FR-033 are modeled. |
| IV. Composition over inheritance | PASS | No new inheritance hierarchies planned. |
| V. Dependency inversion & testability | PASS | `IAIProvider`, `IUserChatRepository`, `IFileStorage`, `IUnitOfWork` defined in Application, implemented in Infrastructure/Persistence. |
| VI. Separation of concerns | PASS | Controllers stay thin; business rules live in MediatR handlers. |
| VII. Convention over configuration | PASS | Follows `docs/ARCHITECTURE.md` solution/folder conventions as-is. |
| §3 Architecture Rules | PASS | Matches the allowed/forbidden dependency matrix exactly; no repository/UoW/DI deviations planned. |
| §5 Database Principles | PASS | `UserChats` migrated to GUID surrogate key + audit columns + soft delete + concurrency token, per convention. |
| §6 API Standards | PASS | Versioned REST (`/api/v1`), RFC 9457 Problem Details, SSE streaming, rate limiting, OpenAPI. |
| §8 Security | **PARTIAL — justified deviation** | Anonymous-access closure, IDOR fixes, password-hash exposure fix, and role enforcement all comply. However, the hardcoded seed-admin password and already-exposed secrets are explicitly **not** remediated this phase, per the stakeholder decision recorded in `spec.md` § Assumptions/Risks. This is a knowing deviation from §8 ("Secrets MUST NOT be stored... in configuration files committed to the repository"), not an oversight. See § Complexity Tracking. |
| §12 CI/CD (Docker artifact generation) | **PARTIAL — justified deviation** | The constitution's CI/CD article expects Docker images built on every merge; this phase deploys to the existing `site4now.net` target via GitHub Actions without containerization, per the stakeholder's explicit scope decision (`spec.md` § Assumptions). See § Complexity Tracking. |
| §9 AI Principles | PASS | Single `IAIProvider`/`OpenAIProvider`, no multi-provider/model-switching introduced (matches FR-022). |
| §10 Testing Standards | PASS | Full pyramid planned per `docs/TESTING.md`; none exists today, closing a total gap (FR-031). |

**Initial gate result**: 2 flagged, stakeholder-approved deviations (credential remediation, Docker deferral) — both already documented and justified in `spec.md`; recorded formally in § Complexity Tracking below. All other constitution areas pass without exception. Proceeding to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/000-legacy-modernization/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
│   └── api-v1.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
Ask Lucy.sln                          # existing legacy solution — kept running/deployable through Migration Strategy step 6
Ask Lucy/                             # existing legacy MVC/Razor project — untouched until parity is confirmed, then decommissioned

src/
├── AskLucy.Domain/                   # entities, value objects, domain events — no external dependencies
├── AskLucy.Application/              # CQRS commands/queries/handlers, DTOs, validators, interfaces (MediatR, FluentValidation, AutoMapper)
├── AskLucy.Infrastructure/           # IAIProvider→OpenAIProvider, IFileStorage→LocalFileStorage, email sender, JWT/refresh-token services
├── AskLucy.Persistence/              # EF Core DbContext, entity configurations, migrations
└── AskLucy.WebAPI/                   # controllers/minimal APIs, auth middleware, SSE endpoints, Swagger, DI composition root

tests/
├── AskLucy.Domain.Tests/
├── AskLucy.Application.Tests/
├── AskLucy.Infrastructure.Tests/
├── AskLucy.Persistence.Tests/        # Testcontainers-backed SQL Server tests
├── AskLucy.WebAPI.Tests/             # WebApplicationFactory-based API tests
└── AskLucy.E2E.Tests/                # Playwright regression matrix (docs/TESTING.md §36)

frontend/                             # new React 19 + Vite + MUI SPA, replacing Ask Lucy/Views + Scripts/ts
├── src/
│   ├── api/
│   ├── assets/
│   ├── components/
│   ├── features/
│   │   ├── chat/                     # chat UI, voice, PDF/audio/CSV attach, math rendering
│   │   ├── auth/                     # login/register/2FA/social-login screens
│   │   └── profile/                  # profile + avatar management
│   ├── hooks/
│   ├── layouts/
│   ├── pages/
│   ├── routes/
│   ├── services/
│   ├── store/                        # Zustand
│   ├── theme/                        # light/dark MUI theme
│   ├── types/
│   └── utils/
└── tests/

.github/workflows/                    # new: build, lint, test, deploy pipeline (FR-029, FR-030)
```

**Structure Decision**: Add the new Clean Architecture projects (`src/AskLucy.*`) and their tests (`tests/AskLucy.*`) alongside the existing `Ask Lucy.sln`/`Ask Lucy/` legacy project rather than replacing it immediately — this is what lets Migration Strategy steps 1–5 keep the legacy app deployable while the new stack is built up incrementally. The new frontend lives in a sibling `frontend/` directory rather than inside the legacy project so the legacy Razor/webpack build is never touched. The legacy project and its `Views`/`Scripts/ts` are removed only in step 6 (Decommission), once every capability has reached parity.

## Complexity Tracking

> Two constitution deviations are carried forward from `spec.md`, both already stakeholder-approved. Recorded here per the constitution's amendment/compliance-review requirement that complexity against a principle be justified, not silently introduced.

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Hardcoded seed-admin plaintext password and already-committed secrets (`appsettings.json`, `*.PublishSettings`) are **not** remediated in this migration, violating constitution §8 ("Secrets MUST NOT be stored... in configuration files committed to the repository"). | Stakeholder explicitly declined remediation as part of this spec's scope (see `spec.md` § Assumptions/Risks) to keep this phase focused on architecture/behavior parity. | Rotating credentials and removing the seeded account was offered as the recommended option during `/speckit-specify` and explicitly declined by the stakeholder; tracked as an accepted, documented risk rather than silently dropped. **Recommendation**: raise a follow-up ADR and a separate, urgent security-remediation task outside this spec's scope. |
| Docker containerization / Azure App Service cutover, expected by constitution §12 ("Docker images are built for backend and frontend on merge to `master`"), is **not** introduced this phase. | Stakeholder explicitly chose to keep the current `site4now.net` FTP/MSDeploy hosting target and add only a GitHub Actions CI pipeline (see `spec.md` § Assumptions). | Docker/Azure cutover was offered as the recommended option during `/speckit-specify` and explicitly declined ("keep the current deployment and ignore docker"); tracked as a deliberate, phase-scoped divergence. **Recommendation**: raise a follow-up ADR when a future spec revisits hosting. |

No other constitution deviations are introduced by this plan.

## Post-Design Constitution Check

*Re-evaluated after Phase 1 (`data-model.md`, `contracts/api-v1.md`, `quickstart.md`).*

- **Data model** (`data-model.md`): `UserChat` and `RefreshToken` both carry surrogate GUID keys, and `UserChat` carries the full audit/soft-delete/concurrency-token set — consistent with §5. No new aggregate beyond what FR-001–FR-033 require was introduced (avatar storage stayed a field, not a new `Files` aggregate) — consistent with Principle III (Simplicity/YAGNI). **No new violations.**
- **Contracts** (`contracts/api-v1.md`): versioned (`/api/v1`), Problem Details on every error path, every AI endpoint authenticated and rate-limited, admin endpoints DTO-projected (no Identity secrets) and role-gated — consistent with §6/§8. **No new violations.**
- **Quickstart** (`quickstart.md`): validation steps directly exercise the two approved deviations' boundaries (e.g., confirms auth is required, confirms admin data never leaks secrets) without attempting to silently "fix" the two accepted deviations (credential rotation, Docker) outside this spec's approved scope. **No new violations.**

**Post-design gate result**: unchanged from the initial gate — the same 2 stakeholder-approved deviations, no new ones. Ready for `/speckit-tasks`.
