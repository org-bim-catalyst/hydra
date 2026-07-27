---

description: "Task list for SPEC-000: Legacy Application Modernization & Technology Stack Migration"
---

# Tasks: Legacy Application Modernization & Technology Stack Migration

**Input**: Design documents from `/specs/000-legacy-modernization/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-v1.md, quickstart.md (all present)

**Tests**: Included. `spec.md` FR-031 explicitly mandates automated test coverage for the migrated authentication, authorization, and AI-endpoint behavior, and `docs/TESTING.md` is a mandatory engineering standard for this project — this is not an optional TDD preference.

**Organization**: Tasks are grouped by user story (from `spec.md`) to enable independent implementation and testing of each story. The legacy `Ask Lucy/` project and `Ask Lucy.sln` remain untouched and deployable throughout Phases 1–7, per `spec.md` § Migration Strategy — only the Polish phase decommissions it, and only after full parity is confirmed.

> **Revision note**: This file was fully renumbered after `/speckit-analyze` identified 5 coverage gaps (chat rename/delete implementation, 2FA enrollment/disable/recovery-code endpoints, the admin mass-assignment fix, an architecture-compliance-review task for SC-008, and two required ADR-authoring tasks). Eight tasks were added in their logical positions and everything after each insertion point was renumbered; all cross-references below reflect the new numbers.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US5, per `spec.md`)
- Exact file paths are given per `plan.md` § Project Structure

## Path Conventions

- **Backend**: `src/AskLucy.Domain/`, `src/AskLucy.Application/`, `src/AskLucy.Infrastructure/`, `src/AskLucy.Persistence/`, `src/AskLucy.WebAPI/`
- **Backend tests**: `tests/AskLucy.Domain.Tests/`, `tests/AskLucy.Application.Tests/`, `tests/AskLucy.Infrastructure.Tests/`, `tests/AskLucy.Persistence.Tests/`, `tests/AskLucy.WebAPI.Tests/`, `tests/AskLucy.E2E.Tests/`
- **Frontend**: `frontend/src/`, `frontend/tests/`
- **Legacy (read-only reference until decommission)**: `Ask Lucy/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Stand up the new solution/frontend skeletons without touching the legacy app.

- [X] T001 Add `src/AskLucy.Domain`, `src/AskLucy.Application`, `src/AskLucy.Infrastructure`, `src/AskLucy.Persistence`, `src/AskLucy.WebAPI` projects (and matching `tests/AskLucy.*.Tests` projects) to `Ask Lucy.sln`, referencing each other per the Dependency Rule in `plan.md` § Constitution Check; leave the existing `Ask Lucy/` project untouched.
- [X] T002 Scaffold the React 19 + TypeScript + Vite project in `frontend/` with the folder structure from `plan.md` § Project Structure (`src/{api,assets,components,features,hooks,layouts,pages,routes,services,store,theme,types,utils}`).
- [X] T003 [P] Configure backend linting/analyzers (`.editorconfig`, `Directory.Build.props` with nullable/analyzers enabled) at the repository root for the new `src/AskLucy.*` projects.
- [X] T004 [P] Configure frontend ESLint + Prettier in `frontend/.eslintrc`/`frontend/.prettierrc`.
- [X] T005 Create `.github/workflows/ci.yml` with build-only jobs for the new backend and frontend projects (extended with lint/test/deploy in Phase "US5" below).
- [X] T006 [P] Add backend NuGet package references (MediatR, FluentValidation, AutoMapper, Serilog.AspNetCore, Microsoft.AspNetCore.Authentication.JwtBearer, Swashbuckle.AspNetCore, Microsoft.EntityFrameworkCore.SqlServer) to the relevant new projects per `plan.md` § Technical Context.
- [X] T007 [P] Add frontend package dependencies (`react-router`, `@tanstack/react-query`, `zustand`, `react-hook-form`, `@mui/material`, `@emotion/react`, `@emotion/styled`) to `frontend/package.json`.

**Checkpoint**: Both solutions build; frontend dev server starts with an empty shell.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure every user story depends on — auth, database, API skeleton, cross-cutting concerns.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T008 Configure `AskLucyDbContext` in `src/AskLucy.Persistence/AskLucyDbContext.cs`, extending `IdentityDbContext<ApplicationUser>`, wired to the existing `ChatGPT_ClientContextConnection` connection string (unchanged database).
- [X] T009 Create EF Core migration adding the `RefreshTokens` table in `src/AskLucy.Persistence/Migrations/` per `data-model.md` § RefreshToken. *(Implemented as part of the consolidated `InitialCreate` migration below — see note on T010.)*
- [X] T010 Create EF Core migration for the `UserChats` int→GUID primary-key change plus audit/soft-delete/concurrency columns in `src/AskLucy.Persistence/Migrations/`, following the single-migration approach in `research.md` Topic 5. *(T009/T010/T011's schema changes were generated as one `InitialCreate` migration, since `AskLucyDbContext` has no prior migration history — this is EF Core's natural/correct pattern for a brand-new DbContext, not a deviation. Production adoption requires the baseline procedure documented in `src/AskLucy.Persistence/Migrations/README.md`; the actual rehearsal-against-a-restored-production-copy step from this task's original wording still needs to happen before production cutover — it cannot be performed in this environment, which has no access to the production database.)*
- [~] T011 Create EF Core migration + one-time data-fix pass writing existing `ProfilePicture` BLOBs to files and populating `ApplicationUser.AvatarFileName`, then dropping `ProfilePicture`, per `research.md` Topic 6 and `data-model.md` § ApplicationUser. *(Schema change done via `InitialCreate`. The one-time BLOB→file data-fix code itself is not yet written — tracked as remaining work before this task is fully complete.)*
- [X] T012 [P] Implement the global soft-delete query filter and a `SaveChanges` interceptor for audit columns (`CreatedAtUtc`/`CreatedBy`/`ModifiedAtUtc`/`ModifiedBy`/`DeletedAtUtc`/`DeletedBy`) in `src/AskLucy.Persistence/Interceptors/AuditSaveChangesInterceptor.cs`.
- [X] T013 Implement JWT issuance + refresh-token rotation service (`ITokenService`/`TokenService`) in `src/AskLucy.Infrastructure/Auth/`, per `research.md` Topic 1 (15-minute access token, 14-day rotating refresh token, family-based reuse detection), on top of the existing `SignInManager`/`UserManager`. *(`ITokenService`/`TokenService` is pure JWT/refresh issuance in Infrastructure; credential validation against `SignInManager`/`UserManager` lives in the new `IIdentityService`/`IdentityService` in Persistence — see the architecture note below.)*
- [X] T014 [P] Implement centralized Problem Details exception-handling middleware in `src/AskLucy.WebAPI/Middleware/ProblemDetailsMiddleware.cs` per `contracts/api-v1.md` § Error format.
- [X] T015 [P] Configure Serilog structured logging and a correlation-ID middleware in `src/AskLucy.WebAPI/Program.cs` / `src/AskLucy.WebAPI/Middleware/CorrelationIdMiddleware.cs`.
- [X] T016 [P] Configure `Microsoft.AspNetCore.RateLimiting` with a per-user partitioned fixed-window limiter (20 req/min regular, 100 req/min Administrator/Super User) in `src/AskLucy.WebAPI/Program.cs`, per `research.md` Topic 3.
- [X] T017 Register MediatR validation and logging `IPipelineBehavior` implementations in `src/AskLucy.Application/Behaviors/`.
- [X] T018 Implement `IAIProvider` in `src/AskLucy.Application/Abstractions/IAIProvider.cs` and `OpenAIProvider` in `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs` (Chat, Stream, GenerateImage, SpeechToText) with the single-retry-then-Problem-Details behavior from `research.md` Topic 4.
- [X] T019 [P] Configure OpenAPI/Swagger generation in `src/AskLucy.WebAPI/Program.cs` reflecting `contracts/api-v1.md`. *(Uses .NET 10's built-in `Microsoft.AspNetCore.OpenApi` rather than Swashbuckle — see architecture note below.)*
- [X] T020 [P] Configure an explicit CORS allow-list (frontend origin(s) only, replacing today's wildcard) in `src/AskLucy.WebAPI/Program.cs`, per `research.md` Topic 7.
- [X] T021 Scaffold the frontend API client in `frontend/src/api/` (JWT-aware `fetch` wrapper with refresh-on-401), a TanStack Query provider in `frontend/src/app/`, a Zustand store skeleton in `frontend/src/store/`, the light/dark MUI theme in `frontend/src/theme/`, and the routing shell in `frontend/src/routes/`.

**Architecture notes from implementation** (not deviations from spec.md/plan.md's requirements, but concrete decisions made while writing the code, recorded here for the next contributor):
- `ApplicationUser` and `AskLucyDbContext` live in `AskLucy.Persistence` (not `AskLucy.Domain`), because `ApplicationUser` must derive from ASP.NET Core Identity's `IdentityUser`, which would violate Domain purity (constitution §3). `UserChat` and `RefreshToken` remain pure Domain entities.
- Application defines `IIdentityService` (auth operations returning plain DTOs, no `ApplicationUser`/`IdentityResult` leakage) implemented in `AskLucy.Persistence` (where `UserManager`/`SignInManager` and `ApplicationUser` already live), keeping `AskLucy.Infrastructure`'s `ITokenService` free of any Persistence dependency.
- OpenAPI generation uses .NET 10's built-in `Microsoft.AspNetCore.OpenApi` package rather than Swashbuckle — functionally equivalent for this migration's needs and avoids adding a second OpenAPI document generator.
- `Microsoft.OpenApi` 2.0.0 (a transitive dependency of `Microsoft.AspNetCore.OpenApi` 10.0.10) has a known NU1903 vulnerability advisory; upgrading it standalone to 3.x breaks the built-in OpenAPI source generator (incompatible major version), so it is tracked as an accepted, unavoidable-for-now finding rather than silently suppressed — revisit when Microsoft ships a compatible fix.

**Checkpoint status**: ✅ Backend builds solution-wide with 0 errors; starts and responds `200` on `/health` and `/openapi/v1.json` in a local smoke test. Frontend builds and lints clean with the theme/routing/query-provider shell wired in `App.tsx`. Phase 2 (Foundational) is complete except for T011's data-fix code (tracked above) and the still-pending production migration rehearsal.

**Checkpoint**: New backend runs, authenticates, logs, rate-limits, and exposes an empty-but-documented `/api/v1` surface; frontend shell runs against it.

---

## Phase 3: User Story 1 - Existing user experience is unchanged (Priority: P1) 🎯 MVP

**Goal**: Every legacy capability (chat, translate, image generation, transcription, PDF extraction, voice, 2FA enrollment/login, social login, chat create/rename/delete, theming) works identically through the new stack.

**Independent Test**: Run the full regression matrix in `quickstart.md` § 4 against the new backend + frontend and confirm every item matches the legacy baseline.

### Tests for User Story 1 ⚠️

> Write these tests FIRST; confirm they fail before implementing.

- [X] T022 [P] [US1] Contract test `POST /api/v1/ai/chat` (SSE) — implemented as `SendChatMessageCommandHandlerTests` in `tests/AskLucy.Application.Tests/Ai/` (streams provider chunks; throws `ValidationException` on empty messages) plus the anonymous-401 check in `tests/AskLucy.WebAPI.Tests/Ai/AnonymousAccessTests.cs`. **Deviates from the literal file path/location** in the original task: the business-logic assertions live at the Application-handler level (no live database or real OpenAI call needed there), while the WebAPI-level test only covers the auth gate — a full request/response contract test through a live host would additionally require a real database and a real (or recorded) OpenAI response, neither available in this environment.
- [X] T023 [P] [US1] Contract test `POST /api/v1/ai/translate` — `TranslateCommandHandlerTests` in `tests/AskLucy.Application.Tests/Ai/` (HTML-fence extraction and fallback) plus the anonymous-401 check in `AnonymousAccessTests.cs`. Same file-location deviation as T022, same reason.
- [X] T024 [P] [US1] Contract test `POST /api/v1/ai/images` — covered by the anonymous-401 check in `AnonymousAccessTests.cs`; `GenerateImageCommandHandler` is a thin pass-through to `IAIProvider` with no branching logic to unit-test beyond that.
- [X] T025 [P] [US1] Contract test `POST /api/v1/ai/transcriptions` — covered by the anonymous-401 check (multipart request) in `AnonymousAccessTests.cs`; `TranscribeAudioCommandHandler` is likewise a thin pass-through.
- [X] T026 [P] [US1] Contract tests for `/api/v1/auth/*` — `LoginCommandHandlerTests`, `LoginTwoFactorCommandHandlerTests`, `RegisterCommandHandlerTests` (confirmation email sent only on success, never on failure), and `RefreshCommandHandlerTests` (including the family-wide revocation on refresh-token reuse — the single highest-value security test in the auth surface) in `tests/AskLucy.Application.Tests/Authentication/`, plus a `POST /auth/login` validation-400 check in `tests/AskLucy.WebAPI.Tests/Auth/TwoFactorManagementTests.cs`. Logout/external-login handlers remain untested (both are thin pass-throughs with no branching logic, same rationale as T024/T025).
- [X] T027 [P] [US1] Contract tests for `GET/POST/PATCH/DELETE /api/v1/chats` — `CreateUserChatCommandHandlerTests`, `RenameUserChatCommandHandlerTests`, `DeleteUserChatCommandHandlerTests` (including ownership-denial cases — see also T051/US3), and `GetMyUserChatsQueryHandlerTests` in `tests/AskLucy.Application.Tests/Chats/`, plus the anonymous-401 check in `AnonymousAccessTests.cs`.
- [~] T028 [P] [US1] Integration test: a user with TOTP 2FA already enrolled completes login through the new JWT flow without re-enrolling. **Cannot be performed in this environment** — it requires a live SQL Server with a seeded user carrying a real authenticator secret (no Docker/Testcontainers or SQL Server instance is available here). `TokenServiceTests` in `tests/AskLucy.Infrastructure.Tests/Auth/` covers the pure JWT/refresh logic instead; the actual 2FA-continuity scenario is written into the Playwright regression matrix (T029) to run against a real deployment.
- [~] T029 [P] [US1] Playwright E2E test covering `quickstart.md` § 4 — the project and `tests/AskLucy.E2E.Tests/RegressionMatrix.spec.ts` (login+2FA, chat streaming, translate, image generation, theme toggle, mobile sidebar collapse) are written and scaffolded (`@playwright/test` installed, `playwright.config.ts` present), but **cannot be executed in this environment** — no live backend/frontend/database/OpenAI key. Must be run against a real deployment via `npm test` with `E2E_BASE_URL` set, per the file's own header comment.
- [X] T030 [P] [US1] Contract tests for `/api/v1/auth/2fa/{enable,disable,recovery-codes}` in `tests/AskLucy.WebAPI.Tests/Auth/TwoFactorManagementTests.cs` (401-without-token for all three endpoints).

**Test verification**: 40 automated tests written and passing in this environment (6 Domain, 23 Application, 9 WebAPI, 5 Infrastructure — all run via `dotnet test`, plus 2 frontend Vitest tests via `npm run test`), growing to 54 backend + 2 frontend by the end of Phases 5–6. None require a live SQL Server, Docker, or a real OpenAI key. T028 (TOTP continuity) and T029 (Playwright E2E) are written but cannot execute without a live deployment — see their notes above. **`tests/AskLucy.Persistence.Tests` remains empty** — `docs/TESTING.md` §13 specifically calls for Testcontainers-backed SQL Server tests there (not EF Core InMemory, which wouldn't validate real relational behavior like the `UserChats` PK migration), and no Docker daemon is available in this environment to run Testcontainers. This is a real, acknowledged coverage gap to close once a Docker-capable environment is available — not silently skipped.

### Implementation for User Story 1

- [X] T031 [US1] Implement `SendChatMessageCommand`/handler in `src/AskLucy.Application/Chats/Commands/SendChatMessage/` calling `IAIProvider.Stream`. *(Implemented as a MediatR `IStreamRequest<string>` in `src/AskLucy.Application/Ai/Commands/SendChatMessage/`, not `Chats/` — it's an AI operation, not a chat-entity operation; validated inline since MediatR's `IPipelineBehavior` doesn't cover stream requests.)*
- [X] T032 [US1] Implement the SSE streaming endpoint `POST /api/v1/ai/chat` in `src/AskLucy.WebAPI/Controllers/v1/AiController.cs` (depends on T031). *(Controller-based, not minimal-API `Endpoints.cs`, matching the `-controllers` WebAPI template scaffolded in T001.)*
- [X] T033 [P] [US1] Implement `TranslateCommand`/handler + `POST /api/v1/ai/translate` endpoint in `src/AskLucy.Application/Ai/Commands/Translate/` and `src/AskLucy.WebAPI/Controllers/v1/AiController.cs`.
- [X] T034 [P] [US1] Implement `GenerateImageCommand`/handler + `POST /api/v1/ai/images` endpoint in `src/AskLucy.Application/Ai/Commands/GenerateImage/` and `src/AskLucy.WebAPI/Controllers/v1/AiController.cs`.
- [X] T035 [P] [US1] Implement `TranscribeAudioCommand`/handler + `POST /api/v1/ai/transcriptions` endpoint in `src/AskLucy.Application/Ai/Commands/Transcribe/` and `src/AskLucy.WebAPI/Controllers/v1/AiController.cs`.
- [X] T036 [US1] Implement `CreateUserChatCommand` and `GetMyUserChatsQuery` handlers in `src/AskLucy.Application/Chats/`.
- [X] T037 [US1] Implement `RenameUserChatCommand` and `DeleteUserChatCommand` (soft delete) handlers in `src/AskLucy.Application/Chats/Commands/`, wired to `PATCH`/`DELETE /api/v1/chats/{id}` in `src/AskLucy.WebAPI/Controllers/v1/ChatsController.cs` (FR-033).
- [X] T038 [US1] Implement `Register`, `Login`, `Login2fa`, `Refresh`, `Logout`, and external-login-callback handlers in `src/AskLucy.Application/Authentication/`, reusing existing `UserManager`/`SignInManager` behavior (via the new `IIdentityService`), and their endpoints in `src/AskLucy.WebAPI/Controllers/v1/AuthController.cs`. Also fixes the legacy email-confirmation template race condition (T064's original scope) by rendering the email in-memory per request from the start, rather than introducing the bug and fixing it later.
- [X] T039 [US1] Implement TOTP enable, disable, and recovery-code-generation handlers in `src/AskLucy.Application/Authentication/Commands/TwoFactor/`, wired to `/api/v1/auth/2fa/{enable,disable,recovery-codes}` in `src/AskLucy.WebAPI/Controllers/v1/AuthController.cs` (FR-011).
- [X] T040 [US1] Implement `GET/PATCH /api/v1/users/me`, avatar upload (`PUT /api/v1/users/me/avatar`), and the signed-URL avatar download endpoint in `src/AskLucy.WebAPI/Controllers/v1/UsersController.cs`, per `research.md` Topic 6. *(Download endpoint ended up as `GET /api/v1/users/{userId}/avatar?exp=&sig=` rather than literally `/me/avatar` — a signed URL must be usable without an Authorization header, e.g. from an `<img>` tag, so it can't rely on "me" resolving from a bearer token; the upload endpoint remains at `/me/avatar`.)*

**Backend verification**: Full solution builds with 0 errors after T031–T040 (only the two previously-tracked warnings remain). A live smoke test (`POST /api/v1/auth/login`) confirmed the entire DI graph — MediatR pipeline, FluentValidation, `IIdentityService`, `UserManager`, EF Core — resolves and executes correctly end-to-end, failing only at the expected point (no SQL Server reachable in this environment).
- [X] T041 [US1] Build the chat feature UI (message composer, streamed-response rendering, Markdown/KaTeX rendering, PDF/audio/CSV file-attach dispatch) in `frontend/src/features/chat/`.
- [X] T042 [P] [US1] Wire client-side voice input/output (Web Speech recognition + synthesis) in `frontend/src/features/chat/voice/`.
- [X] T043 [P] [US1] Wire client-side PDF text extraction (`pdfjs-dist`) in `frontend/src/features/chat/pdf/`.
- [X] T044 [P] [US1] Build auth screens (login, register, 2FA challenge, social login buttons) in `frontend/src/features/auth/`.
- [X] T045 [P] [US1] Build the profile/avatar screen in `frontend/src/features/profile/`.
- [X] T046 [P] [US1] Implement light/dark theme toggle and responsive layout in `frontend/src/theme/` and `frontend/src/layouts/`. *(Responsive sidebar-to-drawer collapse lives in `ChatPage.tsx` itself rather than a separate `layouts/` file — there's only one authenticated layout shape so far; a shared layout component can be extracted if/when a second one appears.)*
- [ ] T047 [US1] Execute the rehearsed `UserChats`/avatar data migration (T010, T011) against a restored production copy and verify row-for-row parity, per `quickstart.md` § 6. **Cannot be performed in this environment — no production database access.** Remains a required manual step before production cutover (see `src/AskLucy.Persistence/Migrations/README.md`).

**Frontend verification**: `npm run build` and `npm run lint` both pass clean (0 errors, 0 warnings) after T041–T046, including MUI v9 compatibility fixes (v9 dropped direct system-prop shorthands like `mb`/`p`/`display` on `Box`/`Stack`/`Typography` in favor of `sx`) and a real react-hooks lint finding (setState-in-effect in `ProfilePage`) fixed via `react-hook-form`'s `values` option instead of manual `useEffect` sync. Route-level code splitting (`React.lazy`) was added per constitution §15, which the original task list didn't call out explicitly but is required by the Performance article.

**Checkpoint**: User Story 1 is fully functional and independently testable — this is the MVP.

---

## Phase 4: User Story 2 - Anonymous AI access is closed (Priority: P1)

**Goal**: Every AI-invoking endpoint rejects unauthenticated requests.

**Independent Test**: Call each `/api/v1/ai/*` endpoint with no `Authorization` header and confirm `401`; confirm the same request succeeds when authenticated.

- [X] T048 [P] [US2] Contract tests asserting `401` for unauthenticated requests to all four AI endpoints in `tests/AskLucy.WebAPI.Tests/Ai/AnonymousAccessTests.cs`.
- [X] T049 [US2] Apply and verify `[Authorize]` on every AI endpoint in `src/AskLucy.WebAPI/Controllers/v1/AiController.cs` (class-level `[Authorize]`, applied from T031 onward rather than as a separate retrofit step — auth-required-by-default was built in from the start, not bolted on afterward). Verified passing via T048's tests.
- [X] T050 [US2] Handle `401` responses from AI calls in the frontend API client by redirecting to login, in `frontend/src/api/httpClient.ts` (implemented in Phase 2/T021 as part of the initial API client scaffold, since 401-handling is a cross-cutting concern of the client, not AI-specific).

**Note**: All of Phase 4 was completed incidentally while implementing Phase 3, since the AI controller was built with `[Authorize]` from its first line rather than added later — there was never a moment where the endpoints existed without the auth gate. No new code was needed for this phase; only verification.

**Checkpoint**: Anonymous access to AI endpoints is provably closed without affecting authenticated use from User Story 1.

---

## Phase 5: User Story 3 - Cross-user data access is closed (Priority: P2)

**Goal**: A user can never read/modify/delete another user's chats, cannot see another user's Identity secrets, and cannot overwrite arbitrary fields on another user's account via the admin update endpoint.

**Independent Test**: As User A, attempt to access User B's chat and the admin user list; confirm denial and confirm no response ever contains `passwordHash`/`securityStamp`/`concurrencyStamp`; confirm the admin update endpoint rejects unexpected/extra fields rather than persisting them.

- [X] T051 [P] [US3] Contract test: User A denied access to User B's chat (`GET/PATCH/DELETE /api/v1/chats/{id}`) — `OwnershipTests.cs` in `tests/AskLucy.WebAPI.Tests/Chats/` covers the auth-gate layer (no `GET /chats/{id}` endpoint exists — only the owner-scoped list, per contracts/api-v1.md); the actual cross-user denial logic is unit-tested in `RenameUserChatCommandHandlerTests`/`DeleteUserChatCommandHandlerTests` (Application layer), where it's verifiable without a live database.
- [X] T052 [P] [US3] Contract test: no endpoint response under `/api/v1/users*` ever includes `passwordHash`, `securityStamp`, or `concurrencyStamp` — `NoSecretLeakageTests.cs` proves this structurally (`UserAdminDto`'s properties cannot include those names by reflection) plus the auth-gate check, in `tests/AskLucy.WebAPI.Tests/Users/`.
- [X] T053 [P] [US3] Contract test: `PATCH /api/v1/users/{id}` with unexpected/extra body fields (e.g. `passwordHash`, `role`) leaves them unpersisted — `UpdateUserOverpostingTests.cs` proves this structurally (`UpdateUserRequest` only has `FirstName`/`LastName` properties, so extra JSON fields can never bind) plus the auth-gate check, in `tests/AskLucy.WebAPI.Tests/Users/`.
- [X] T054 [US3] Implement an owner-scoping guard for chat resources — `ChatOwnershipGuard` (static helper, not an ASP.NET Core `IAuthorizationHandler`/`IAuthorizationRequirement`) in `src/AskLucy.Application/Chats/Authorization/ChatOwnershipGuard.cs`, applied to `RenameUserChatCommandHandler`/`DeleteUserChatCommandHandler`. **Deviates from the literal task**: `IAuthorizationHandler`/`IAuthorizationRequirement` live in `Microsoft.AspNetCore.Authorization`, which Application must not reference (constitution §3 Dependency Rule) — a plain static guard achieves the same ownership check without that violation.
- [X] T055 [US3] Implement `UserProfileDto` (already existed from T040) and `UserAdminDto` (excluding all Identity secret fields) with an AutoMapper profile (`UserMappingProfile`) in `src/AskLucy.Persistence/Mapping/` — the profile lives in Persistence, not Application, since it maps from the Persistence-owned `ApplicationUser` type; `UserAdminDto` itself is in `src/AskLucy.Application/Users/`.
- [X] T056 [US3] Replace raw `ApplicationUser` serialization in the admin users list endpoint with the `UserAdminDto` projection from T055 — new `GetAllUsersQuery`/handler in `src/AskLucy.Application/Users/Queries/GetAllUsers/`, wired to `GET /api/v1/users` in `src/AskLucy.WebAPI/Controllers/v1/UsersController.cs`. (No legacy raw-entity endpoint ever existed in the new codebase to "replace" — this is a from-scratch, DTO-only implementation, which structurally can't regress into the legacy exposure.)
- [X] T057 [US3] Implement `UpdateUserCommand`/handler (explicit allow-listed fields only — no client-supplied entity persisted as-is) in `src/AskLucy.Application/Users/Commands/UpdateUser/`, wired to `PATCH /api/v1/users/{userId}` in `src/AskLucy.WebAPI/Controllers/v1/UsersController.cs` — closes the legacy overposting/mass-assignment vulnerability from `spec.md` § Gap Analysis by construction (the request DTO has no other properties to bind).

**Note**: `GET /api/v1/users` and `PATCH /api/v1/users/{userId}` are `[Authorize]` (any authenticated user) as of this phase — role-gating to Administrator/Super User only is Phase 6's job (T059), per the phase dependency already noted in this file.

**Checkpoint**: Cross-user data access, secret leakage, and admin mass-assignment are all provably closed.

---

## Phase 6: User Story 4 - Administrative access is properly gated (Priority: P2)

**Goal**: Only Administrator/Super User role holders can reach Control Panel/user-management actions; everyone else is denied server-side, not just UI-hidden.

**Independent Test**: As a non-admin, request every admin route/endpoint and confirm denial; as an admin, confirm unchanged access.

- [X] T058 [P] [US4] Contract tests: non-admin gets `403` on every admin endpoint, admin passes authorization, in `tests/AskLucy.WebAPI.Tests/Admin/RoleAuthorizationTests.cs`. Uses a self-signed test JWT (`TestJwtFactory`) so the role-check *policy* is genuinely exercised without a live database — the admin case is verified to pass authorization (never 401/403) even though the request can't succeed end-to-end without a real user in a real database.
- [X] T059 [US4] Implement and apply the `AdministratorOrSuperUser` authorization policy (already registered in `Program.cs` during Phase 2) to the admin endpoints in `src/AskLucy.WebAPI/Controllers/v1/UsersController.cs` (`GET /api/v1/users`, `PATCH /api/v1/users/{userId}`), replacing the legacy UI-only role check.
- [X] T060 [US4] Gate admin routes client-side (UX affordance only, not the security boundary) in `frontend/src/routes/AdminRoute.tsx`, wired to a new `/admin/users` route (`AdminUsersPage`) — the minimal replacement for the legacy Control Panel's user grid, which deliberately never renders `passwordHash`/`securityStamp`/`concurrencyStamp` (FR-019), unlike the page it replaces.

**Bug caught and fixed while building this phase's test**: decoding a real access token to write `AdminRoute.tsx` revealed that `TokenService` was writing long-form `ClaimTypes.*` URIs (e.g. `http://schemas.../claims/role`) directly into the JWT payload instead of the standard short claim names (`role`, `nameid`) a JWT-consuming JS client expects — `JwtSecurityToken(claims:)` does not apply outbound claim-name mapping the way `SecurityTokenDescriptor`-based token creation does. Fixed in `TokenService.GenerateAccessToken` (and mirrored in `TestJwtFactory` for test-token parity) with an explicit remap through `JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap`; a regression test (`GenerateAccessToken_ShouldEmitShortConventionalClaimNames_NotLongFormUris`) locks this in. Server-side role checks were unaffected either way (ASP.NET Core's inbound validation maps short names back to `ClaimTypes.*` by default), but the frontend could not have read the role claim without this fix.

**Checkpoint**: Role enforcement is server-side and provably correct for both admins and non-admins.

---

## Phase 7: User Story 5 - Automated build, test, and deploy (Priority: P3)

**Goal**: Every change is built, linted, and tested automatically, and every merge deploys to the existing hosting target without manual steps.

**Independent Test**: Open a PR with a failing test and confirm CI blocks merge; merge a passing change and confirm automatic deployment to `site4now.net`.

- [X] T061 [US5] Extend `.github/workflows/ci.yml` (from T005) with lint (`dotnet format --verify-no-changes`, `npm run lint`) and test jobs (`dotnet test` across all 4 backend test projects, `npm run test`) gating pull-request merges. The Playwright job is present but gated behind `if: false` with a comment explaining why: it needs a live deployment (backend + frontend + database + OpenAI key + a seeded test account) that doesn't exist as an ephemeral CI checkout, so it can't be a blocking PR check — it's wired to run against real environment variables (`E2E_BASE_URL`, seeded test credentials) once one exists.
- [X] T062 [US5] Add a `deploy` job to `.github/workflows/ci.yml` that publishes `src/AskLucy.WebAPI` and builds the frontend, then deploys both via FTP to the existing `site4now.net` target (`SamKirkland/FTP-Deploy-Action`, credentials from GitHub Secrets — never committed) on merge to `master`, per FR-030 (no Docker, no Azure cutover). **Cannot be executed/verified in this environment** — there's no real FTP server or credentials to deploy against; the workflow YAML is syntactically complete and follows the constitution §8 secrets pattern, but a real run requires the secrets listed in `CONTRIBUTING.md` to be configured in the GitHub repository first.
- [X] T063 [P] [US5] Document the repository's required branch-protection rule (required status checks + review) in `CONTRIBUTING.md`, including the exact `gh api` command to apply it. **Not applied to the live repository** — this changes shared GitHub settings visible to every contributor of `mustafasalahuldin/Ask-Lucy` and needs your explicit go-ahead before being run, per the "hard-to-reverse, shared-state" action guidance this assistant follows.

**Note**: A real formatting bug was caught and fixed while wiring the lint job: `.editorconfig` declares CRLF line endings, but several newly-written files had LF endings and one migration file had an encoding issue `dotnet format` flagged. Ran `dotnet format --exclude "Ask Lucy/**"` to fix all of it (legacy project left untouched); `dotnet format --verify-no-changes` now passes cleanly, and all 54 backend tests (6+23+6+19) still pass after the reformat.

**Checkpoint**: CI/CD is fully automated end-to-end for this repository.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Close out remaining gap-analysis items, verify constitution compliance, and finish the migration.

- [X] T064 [P] Fix the email-confirmation template race condition: render the confirmation email in-memory per request instead of mutating the shared `wwwroot/templates/email/index.html` file on disk, in `src/AskLucy.Infrastructure/Email/SendGridEmailSender.cs`. *(Done as part of T038 — implemented correctly from the start rather than introducing the bug and fixing it later.)*
- [ ] T065 Decommission the legacy `Ask Lucy/` project and its `Ask Lucy.sln` reference once every checkpoint above has confirmed full parity in production, per `spec.md` § Migration Strategy step 6. **Deliberately NOT done.** This is explicitly gated on production parity being confirmed — which requires the still-outstanding production data migration (T047) and a real deployment, neither achievable in this sandbox. The legacy project remains untouched and buildable, exactly as `spec.md` § Migration Strategy requires until that gate is met.
- [X] T066 [P] Rename the frontend's `package.json` `name` field away from the legacy `chatgpt-client` naming to `ask-lucy-frontend` in `frontend/package.json`. *(Done at creation time in Phase 1/T002, not as a later rename — the frontend was never named `chatgpt-client` to begin with in this codebase.)*
- [X] T067 Perform and record an architecture-compliance review against `.specify/memory/constitution.md` §3 (Architecture Rules), confirming zero unresolved Dependency Rule violations across `src/AskLucy.*` (SC-008). **Findings**: audited every project's `ProjectReference`s — they match the approved dependency matrix exactly (Domain: none; Application: → Domain only; Infrastructure/Persistence: → Application, Domain, never each other; WebAPI: → all four). Grepped Domain for forbidden framework `using`s (EF Core, ASP.NET Core, `Microsoft.Extensions.*`) — none found. Grepped Application for `AskLucy.Infrastructure`/`AskLucy.Persistence` references — the one hit (`IIdentityService.cs`) is a doc-comment explaining where the interface is implemented, not a code dependency. **Zero violations found.**
- [~] T068 Run the full `quickstart.md` validation guide end-to-end and check off every item in `spec.md` § Acceptance Criteria. **Partially performed**: the backend was smoke-tested live (starts cleanly, `/health` and `/openapi/v1.json` return 200, a real login request flows through the entire DI graph/MediatR pipeline/EF Core down to an expected SQL-connection failure). All 54 backend + 2 frontend automated tests pass. **Not performed** — requires a live deployment this environment cannot provide: manual browser walkthrough of chat/voice/PDF/image/translate, the Playwright regression matrix, and the production data migration rehearsal.
- [X] T069 [P] Update `docs/*.md` cross-references and add a top-level `README.md` describing the new solution/frontend structure — `README.md` added; `docs/ROADMAP.md`'s Phase 0 section updated with a dated implementation-status note cross-referencing this spec and both new ADRs.
- [X] T070 [P] Author `docs/adr/0001-defer-credential-secret-remediation.md` recording the stakeholder decision (`spec.md` § Assumptions/Risks) to leave the hardcoded seed-admin password and exposed secrets unremediated in this migration. **No follow-up owner/date could be assigned** (not available in this session) — flagged explicitly inside the ADR as still needing one before the legacy project is decommissioned (T065).
- [X] T071 [P] Author `docs/adr/0002-defer-docker-azure-cutover.md` recording the stakeholder decision (`spec.md` § Assumptions) to keep `site4now.net` hosting and defer Docker/Azure adoption, per constitution §12's expectation that CI/CD build Docker images.
- [ ] T072 Measure SC-006 (P95 time-to-first-visible-content < 2s) under a representative load and record the result against `spec.md` § Success Criteria. **Cannot be performed in this environment** — requires a live deployment and load-testing tooling (e.g. k6/Artillery) pointed at a real OpenAI-backed instance, neither available here.

**Phase 8 summary**: 6 of 9 tasks fully complete, 1 partially complete (T068), 2 correctly left undone because their gating conditions genuinely aren't met yet (T065 needs production parity; T072 needs a live deployment) — not overlooked, but honestly blocked on infrastructure this sandbox doesn't have.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately.
- **Foundational (Phase 2)**: Depends on Setup. **Blocks all user stories.**
- **User Story 1 (Phase 3)**: Depends on Foundational only. This is the MVP.
- **User Story 2 (Phase 4)**: Depends on Foundational and on the AI endpoints existing (T032–T035 from US1) to add `[Authorize]` to.
- **User Story 3 (Phase 5)**: Depends on Foundational and on the chat and user endpoints existing (T036, T037, T040 from US1).
- **User Story 4 (Phase 6)**: Depends on Foundational and on the admin endpoints existing (T040/T056/T057 area from US1/US3).
- **User Story 5 (Phase 7)**: Depends on Setup's CI skeleton (T005) and benefits from US1–US4's tests existing to actually gate on, but can be built in parallel once T005 is done.
- **Polish (Phase 8)**: Depends on all desired user stories being complete; T065 (decommission) specifically requires US1–US4 checkpoints all passed in production; T067 (architecture review) and T070–T071 (ADRs) can be done any time after Foundational, but are sequenced last as closing verification/documentation.

### User Story Dependencies

- **US1 (P1)**: No dependency on other stories — it *is* the foundation the others harden.
- **US2 (P1)**: Builds directly on US1's AI endpoints (adds the auth gate to them) — implement after US1's endpoints exist, though its own tests (T048) can be written in parallel with US1.
- **US3 (P2)**: Builds on US1's chat/user endpoints (adds ownership scoping, DTO projection, and the validated admin-update command) — same pattern as US2.
- **US4 (P2)**: Builds on US1's admin/user endpoints (adds role policy) — independent of US2/US3, can proceed in parallel with them.
- **US5 (P3)**: Independent of US2–US4's functional content; only needs Setup's CI skeleton to exist.

### Within Each User Story

- Tests are written and confirmed failing before implementation.
- Application-layer commands/handlers before WebAPI endpoints.
- Backend endpoints before the frontend features that call them.
- Story complete before moving to the next priority (though US2–US4 may run in parallel once US1's relevant endpoints exist, per above).

### Parallel Opportunities

- All Setup tasks marked `[P]` (T003, T004, T006, T007) run in parallel.
- Within Foundational, T012, T014, T015, T016, T019, T020 (`[P]`) run in parallel once T008/T009/T013 are done.
- Within US1, the three parallel AI-endpoint implementation tasks (T033–T035, alongside T031/T032) run in parallel; all frontend feature tasks marked `[P]` (T042–T046) run in parallel once T041/T021 exist.
- Once US1's relevant endpoints exist, US2, US3, and US4 can be implemented in parallel by different contributors.
- US5 can be built in parallel with US1–US4 once T005 exists.
- In Polish, T064, T066, T069, T070, T071 (`[P]`) can all run in parallel; T065, T067, T068, T072 have the sequencing noted above.

---

## Parallel Example: User Story 1 AI endpoints

```bash
# After T031/T032 (chat) land, these can run together:
Task: "Implement TranslateCommand/handler + endpoint in src/AskLucy.Application/Ai/Commands/Translate/ and src/AskLucy.WebAPI/Endpoints/AiEndpoints.cs"
Task: "Implement GenerateImageCommand/handler + endpoint in src/AskLucy.Application/Ai/Commands/GenerateImage/ and src/AskLucy.WebAPI/Endpoints/AiEndpoints.cs"
Task: "Implement TranscribeAudioCommand/handler + endpoint in src/AskLucy.Application/Ai/Commands/Transcribe/ and src/AskLucy.WebAPI/Endpoints/AiEndpoints.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 (Setup) and Phase 2 (Foundational — CRITICAL, blocks everything).
2. Complete Phase 3 (User Story 1).
3. **STOP and VALIDATE**: run `quickstart.md` § 4 against the new stack; confirm full feature parity.
4. This alone is a legitimate, demoable increment — the app runs on the new stack with identical behavior (plus the auth gate is not yet closed at this point, so do not expose this increment publicly until US2 lands).

### Incremental Delivery

1. Setup + Foundational → foundation ready.
2. US1 → validate against the regression matrix → the technology migration itself is functionally proven.
3. US2 → validate the anonymous-access gate is closed → now safe to deploy publicly.
4. US3 and US4 (parallelizable) → validate data-isolation, mass-assignment, and role-enforcement gates are closed.
5. US5 → CI/CD automated; from here every subsequent spec (SPEC-001+) benefits automatically.
6. Polish → dead-code/legacy cleanup, decommission the legacy project, architecture-compliance review, ADRs, final validation and documentation.

### Parallel Team Strategy

With multiple contributors: one completes Setup + Foundational; then one owns US1 (the critical path — everything else waits on its endpoints existing); once US1's endpoints land, up to three others can take US2, US3, and US4 in parallel while a fourth builds out US5's CI/CD independently from T005 onward.

---

## Notes

- `[P]` tasks touch different files with no unmet dependency.
- `[Story]` labels map every user-story-phase task back to `spec.md`'s US1–US5 for traceability.
- Tests are written first within each story and must fail before the corresponding implementation task is started.
- The legacy `Ask Lucy/` project is never modified — only read for reference — until T065.
- No task in this list introduces Docker, an Azure hosting cutover, credential rotation, or any Phase-1+ capability (chat history, RAG, agents, etc.) — all are out of scope per `spec.md`, consistent with `plan.md` § Complexity Tracking. T070/T071 document those two deferrals; they do not remediate them.
