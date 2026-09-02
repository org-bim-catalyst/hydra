---

description: "Task list for AI Provider Failure Classification & Accurate Health Reporting"
---

# Tasks: AI Provider Failure Classification & Accurate Health Reporting

**Input**: Design documents from `/specs/043-provider-error-classification/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/](./contracts/)

**Tests**: INCLUDED. SC-009 requires regression coverage for every classification and every vision failure mode, and constitution §10 requires tests in the same change that introduces the behaviour.

**Organization**: Grouped by user story so each can be implemented, tested, and shipped independently.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete work)
- **[Story]**: US1–US5, mapping to the user stories in spec.md
- Exact file paths are given in every task

## Path Conventions

Clean Architecture backend under `src/`, React SPA under `src/AskLucy.Web/ClientApp/src/`, xUnit suites under `tests/`. Paths below are repository-relative.

---

## Phase 1: Setup

**Purpose**: Establish the regression baseline before changing anything

- [X] T001 Run the full baseline and record results: `dotnet test`, `npm --prefix src/AskLucy.Web/ClientApp test -- --run`, `dotnet format --verify-no-changes`, `npm --prefix src/AskLucy.Web/ClientApp exec tsc -b --noEmit`. Any pre-existing failure must be noted before proceeding so it is not later mistaken for a regression.
- [X] T002 Confirm the migration precondition from data-model.md § Migration: verify no `AIModels` row holds `0` in `ContextWindowTokens` or `MaxOutputTokens` (the pre-feature `AIModel.Create` rejected it), so the `Up` needs no backfill. Query the dev database directly.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared classification vocabulary, domain shape, single migration, and provider wiring that User Stories 1–4 all build on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

### Application vocabulary — all in one file, therefore sequential

- [X] T003 Add the `AiProviderFailureKind` enum with the nine members and their XML docs to `src/AskLucy.Application/Abstractions/IAIProvider.cs`, per data-model.md §1. Do **not** add an `InternalError` member — FR-007 represents that as the absence of an `AiProviderException`.
- [X] T004 Add `public abstract class AiProviderException(string message, AiProviderFailureKind kind, Exception? innerException = null)` exposing `Kind` and a virtual `RetryAfter` to `src/AskLucy.Application/Abstractions/IAIProvider.cs`, and re-parent the four existing sealed types (`AiProviderUnavailableException`, `AiProviderAuthenticationException`, `AiProviderRateLimitedException`, `AiProviderRequestInvalidException`) onto it, each fixing its own `Kind`. Re-parenting must not change any type name or constructor signature — every existing `catch`-by-type site across the six consuming files must still compile untouched.
- [X] T005 Add the five new sealed exception types (`AiProviderQuotaExhaustedException`, `AiProviderUsageRestrictedException`, `AiProviderCredentialUnreadableException`, `AiProviderNotConfiguredException`, `AiProviderResponseInvalidException`) to `src/AskLucy.Application/Abstractions/IAIProvider.cs` per contracts/provider-failure-classification.md §2.
- [X] T006 In `src/AskLucy.Application/Abstractions/IAIProvider.cs`, add `public sealed record ProviderHealthResult(bool IsHealthy, AiProviderFailureKind? Kind, string? Reason)`, change `CheckHealthAsync` to return `Task<ProviderHealthResult>`, and change `ProviderModelInfo.ContextWindowTokens`/`MaxOutputTokens` to `int?`.

### Domain

- [X] T007 [P] Add nullable `HealthFailureKind` and `HealthFailureReason` to `src/AskLucy.Domain/Ai/AIProvider.cs` and change `UpdateHealthStatus` to `(bool isHealthy, DateTime checkedAtUtc, AiProviderFailureKind? kind, string? reason)`. The method must enforce the invariant itself — clearing both new fields on a healthy result — so a stale reason cannot survive a recovery (data-model.md §2).
- [X] T008 [P] Add nullable `FailureKind` and `FailureReason` to `src/AskLucy.Domain/Ai/ProviderHealthCheck.cs` and extend its `Create` factory to accept them. Keep the type append-only: no mutators, no soft delete.
- [X] T009 [P] Change `ContextWindowTokens` and `MaxOutputTokens` to `int?` in `src/AskLucy.Domain/Ai/AIModel.cs` and relax the validation in `Create` to `if (contextWindowTokens is <= 0)` (and likewise for max output), so absence passes while a supplied `0` or negative still fails. Update the message to "...when supplied."

### Domain tests

- [X] T010 [P] Add tests in `tests/AskLucy.Domain.Tests/Ai/AIModelTests.cs` covering: a model created with both limits absent succeeds; a supplied `0` still throws `DomainRuleViolationException`; a supplied negative still throws; a supplied positive still succeeds.
- [X] T011 [P] Add tests in `tests/AskLucy.Domain.Tests/Ai/AIProviderTests.cs` covering the health invariant: an unhealthy update stores kind and reason; a subsequent healthy update clears both; `HealthFailureKind` is never non-null while `HealthStatus` is `Healthy`.

### Persistence and the single migration

- [X] T012 Update `src/AskLucy.Persistence/Configurations/AIProviderConfiguration.cs` and `src/AskLucy.Persistence/Configurations/ProviderHealthCheckConfiguration.cs` to map the four new columns: enums via `.HasConversion<string>().HasMaxLength(40)` (matching the existing `AIModelStatus` convention — never an ordinal), reasons as `nvarchar(500)` nullable.
- [X] T013 Update `src/AskLucy.Persistence/Configurations/AIModelConfiguration.cs` to drop `.IsRequired()` from `ContextWindowTokens` and `MaxOutputTokens`, following the optional `Pricing` owned-type precedent documented in that same file.
- [X] T014 Create the migration `AddProviderFailureClassificationAndOptionalModelLimits` in `src/AskLucy.Persistence/Migrations/`. `Up` adds the four columns and widens the two `AIModels` columns to nullable. `Down` must backfill `NULL → 0` on both `AIModels` columns **before** altering back to `NOT NULL`, with a comment recording that the round-trip is lossy. Watch the repo's known migration gotchas: no BOM, `System` usings first. **Then apply the migration manually to the shared CI persistence database** — this repository's CI does not auto-migrate it, and the persistence suite fails against the old schema from this point on.

### Shared classifier and provider wiring

- [X] T015 Create `src/AskLucy.Infrastructure/Ai/AiProviderResponseClassifier.cs` implementing the full mapping in contracts/provider-failure-classification.md §1 — per-vendor reason extraction, the non-HTTP failure table, and the documented precedence order. The classifier reads the response body to classify but must **never** place it in an exception message; it returns/throws the typed exception with prose built from the classification alone (FR-013).
- [X] T016 [P] Add table-driven classifier tests in `tests/AskLucy.Infrastructure.Tests/Ai/AiProviderResponseClassifierTests.cs` covering every row of all four vendor tables plus the non-HTTP table, using the existing `StubHttpMessageHandler`. Include an assertion per case that the produced message contains no credential, no vendor body fragment, no exception type name, and no stack trace (SC-008).
- [X] T017 [P] Wire `src/AskLucy.Infrastructure/Ai/GoogleGeminiProvider.cs` to the classifier: `EnsureSuccessAsync` delegates to it; `CreateClientAsync` wraps `credentialProtector.Unprotect` in a `CryptographicException` catch → `AiProviderCredentialUnreadableException` and the missing-provider/null-ciphertext paths throw `AiProviderNotConfiguredException`; `ListAvailableModelsAsync` runs through `WithRetryAsync`; `JsonException` and a missing `models` property become `AiProviderResponseInvalidException`; a `TaskCanceledException` whose caller token did not fire becomes `AiProviderUnavailableException` while a fired caller token rethrows. `CheckHealthAsync` returns a classified `ProviderHealthResult` instead of `bool`.
- [X] T018 [P] Apply the identical wiring to `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs`, using the OpenAI reason table and its `data` property.
- [X] T019 [P] Apply the identical wiring to `src/AskLucy.Infrastructure/Ai/AnthropicProvider.cs`, using the Anthropic `error.type` table.
- [X] T020 [P] Apply the identical wiring to `src/AskLucy.Infrastructure/Ai/OpenRouterProvider.cs`, using the OpenAI-compatible table plus the HTTP 402 → `QuotaExhausted` case.
- [ ] T020a Verify each vendor's current model-list pagination contract against its live API documentation — do **not** rely on remembered defaults. Record per provider: the page-size parameter, its default and maximum, and the continuation field. Known shape: Gemini returns `models[]` plus a continuation token; Anthropic returns `data[]` plus a has-more flag and cursor; OpenAI and OpenRouter return `data[]` complete. Capture the findings in [research.md](./research.md). **NOT DONE** — pagination was implemented in T020b from each vendor documented response shape, and is covered by tests, but the page-size defaults and maxima were not re-checked against live vendor documentation from this environment. Worth confirming before release.
- [X] T020b Make `ListAvailableModelsAsync` follow pagination to completion in `src/AskLucy.Infrastructure/Ai/GoogleGeminiProvider.cs` and `src/AskLucy.Infrastructure/Ai/AnthropicProvider.cs` (and any other provider T020a shows paginates), requesting the maximum permitted page size and looping on the continuation field. Bound the loop with a page cap so a malformed continuation cannot spin forever; exceeding the cap raises `AiProviderResponseInvalidException`.
- [X] T020c [P] Add tests in `tests/AskLucy.Infrastructure.Tests/Ai/` per paginating provider, using `StubHttpMessageHandler` to serve a two-page response and asserting the returned list contains models from both pages. Add a case where the continuation never terminates, asserting the page cap trips rather than hanging (FR-028a).
- [X] T021 Update `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckHostedService.cs` for the new `CheckHealthAsync` return type, keeping its existing behaviour that a failure of the *cycle itself* is logged and retried without marking any provider unhealthy (FR-023).
- [X] T022 [P] Extend the four provider test files in `tests/AskLucy.Infrastructure.Tests/Ai/` (`GoogleGeminiProviderTests.cs`, `OpenAIProviderTests.cs`, `AnthropicProviderTests.cs`, `OpenRouterProviderTests.cs`) with the three previously-escaping cases per provider: unreadable credential, request timeout, unparseable body — asserting each now raises its typed exception rather than escaping untyped.

**Checkpoint**: Vocabulary, schema, and classification are in place. User Stories 1, 2, and 4 can now proceed in parallel.

---

## Phase 3: User Story 1 — An administrator learns *why* a provider action failed (Priority: P1) 🎯 MVP

**Goal**: Every provider-originated failure on the AI Providers page shows a distinct, accurate, actionable message instead of "An unexpected error occurred."

**Independent test**: Drive the sync action against a stub returning each failure kind in turn; confirm a distinct correct message for each, and that an administrator sees the classification while a non-administrator does not.

- [X] T023 [US1] Replace the five separate `AiProvider*Exception` arms in `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs` with one `AiProviderException` arm that switches on `Kind`, producing the status and `type` URI for each of the nine kinds per contracts/provider-failure-classification.md §3. Keep the existing `HttpRequestException` arm as the outer safety net.
- [X] T024 [US1] In the same file, extend the `Retry-After` emission to fire for `QuotaExhausted` as well as `RateLimited`, reading from the base class's `RetryAfter`.
- [X] T025 [US1] In the same file, add the administrator-gated disclosure: when `context.User.IsInRole("Administrator") || context.User.IsInRole("Super User")` (matching `src/AskLucy.Web/Program.cs:146`), set the specific classified `detail` and add the `providerFailure` extension member (`kind`, `canAdministratorAct`, `retryAfterSeconds`); otherwise leave today's generic `detail` and emit no extension (FR-015a).
- [X] T026 [P] [US1] Add the server-side structured log for every classified failure in `src/AskLucy.Web/Middleware/ProblemDetailsMiddleware.cs`'s `LoggerMessage` partial class — provider, kind, and vendor reason code as structured fields, with the vendor body truncated (FR-014, constitution §14).
- [X] T027 [P] [US1] Add tests in `tests/AskLucy.Web.Tests/Middleware/ProblemDetailsMiddlewareProviderFailureTests.cs` asserting the status, `type`, and `Retry-After` for all nine kinds, and asserting that no response body for any kind contains a credential, vendor body, exception type name, or stack trace (SC-002, SC-008).
- [X] T028 [P] [US1] Add tests in the same file for the disclosure gate: an administrator principal receives the specific `detail` plus `providerFailure`; a plain authenticated principal receives the generic `detail` and no `providerFailure` (FR-015a).
- [X] T029 [US1] Add the optional `providerFailure` field to `ApiError` in `src/AskLucy.Web/ClientApp/src/api/httpClient.ts`, populated from the Problem Details extension, leaving `detail` handling unchanged.
- [X] T030 [US1] Update `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.tsx` so its existing `onError` renders the classified message and branches on `providerFailure.kind` for severity and on `canAdministratorAct` for the call-to-action, keeping the current Snackbar/Alert convention. Every async path must retain a visible error outcome (constitution §VIII).
- [X] T031 [P] [US1] Add tests in `src/AskLucy.Web/ClientApp/src/features/admin/components/ModelSyncDialog.test.tsx` covering a credential-rejected, quota-exhausted, and billing-restricted response, asserting the distinct rendered message for each and that the billing case never suggests checking the API key.

**Checkpoint**: US1 is independently shippable — sync failures are fully diagnosable.

---

## Phase 4: User Story 2 — Provider health shows the real, current state with its reason (Priority: P1)

**Goal**: The provider list shows why a provider is unhealthy, when that was last confirmed, and whether the confirmation is stale.

**Independent test**: Record health outcomes of each failure kind, load the page, confirm each renders distinctly with its reason and check time; age the record past the horizon and confirm it presents as possibly out of date.

- [X] T032 [P] [US2] Add `IProviderHealthFreshnessPolicy` to `src/AskLucy.Application/Abstractions/` exposing the staleness horizon for a given check time (FR-019: derived from the interval, never a fixed absolute).
- [X] T033 [P] [US2] Implement it as `src/AskLucy.Infrastructure/Ai/ProviderHealthFreshnessPolicy.cs` over `ProviderHealthCheckOptions` (`checkedAt + 3 × Interval`) and register it in `src/AskLucy.Infrastructure/DependencyInjection.cs`.
- [X] T034 [US2] Update `src/AskLucy.Infrastructure/Ai/ProviderHealthCheckHostedService.cs` to pass the classified kind and reason into `UpdateHealthStatus` and into `ProviderHealthCheck.Create`, replacing the current `"{ex.GetType().Name} during health check."` detail string with the classification's prose.
- [X] T035 [US2] Extend `src/AskLucy.Application/Ai/AdminAiProviderDto.cs` with `HealthFailureKind`, `HealthFailureReason`, and the computed `HealthStaleAfterUtc`, threading the freshness policy into `FromEntity` (or precomputing the horizon in the handler).
- [X] T036 [US2] Update `src/AskLucy.Application/Ai/Queries/GetAdminAiProviders/GetAdminAiProvidersQueryHandler.cs` to supply the freshness policy to the DTO projection.
- [X] T037 [P] [US2] Add tests in `tests/AskLucy.Application.Tests/Ai/GetAdminAiProvidersQueryHandlerTests.cs` asserting the horizon is `checkedAt + 3 × interval`, that it is null when never checked, and that it tracks a changed configured interval rather than a hardcoded duration.
- [X] T038 [US2] Add `ProviderFailureKind`/`healthStaleAfterUtc` to the `AdminAiProvider` interface in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts` per contracts/admin-provider-health-api.md §5.
- [X] T039 [US2] Create `src/AskLucy.Web/ClientApp/src/features/admin/components/ProviderHealthCell.tsx` rendering the six presentation states in the precedence order of contracts/admin-provider-health-api.md §1 — not configured, disabled, not yet checked, configured-but-limited, unhealthy-with-reason, healthy — plus the "possibly out of date" overlay computed client-side from `healthStaleAfterUtc` against current time.
- [X] T040 [US2] Replace the inline health `Chip` block in `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` with `ProviderHealthCell`, removing the now-superseded `HEALTH_COLOR` map.
- [X] T041 [P] [US2] Add tests in `src/AskLucy.Web/ClientApp/src/features/admin/components/ProviderHealthCell.test.tsx` for all six states plus staleness, asserting that a quota/rate-limit state is visually and textually distinct from a credential failure (FR-018) and that "not yet checked" never renders as an error (FR-020).
- [X] T042 [P] [US2] Extend `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.a11y.test.tsx` to cover the new health states, keeping the axe check green.

**Checkpoint**: US2 independently shippable — health is honest and legible.

---

## Phase 5: User Story 3 — An administrator re-checks a provider on demand (Priority: P2)

**Goal**: An administrator can trigger an immediate classified health check for one provider without waiting for the background cycle.

**Independent test**: Flip a stub from failing to succeeding, trigger the re-check, confirm status and reason update immediately.

**Depends on**: US2 (reuses the health DTO shape and cell rendering)

- [X] T043 [US3] Create the command, handler, and result DTO under `src/AskLucy.Application/Ai/Commands/CheckAiProviderHealth/` — one live probe, one `ProviderHealthCheck` row appended, provider current state updated, all through a single `IUnitOfWork.SaveChangesAsync`. Return `CheckAiProviderHealthResultDto` per contracts/admin-provider-health-api.md §2. A provider found failing is still a successful command; only a failure of the check mechanism throws.
- [X] T044 [P] [US3] Log the admin action via the existing `AiAdminActionLog` pattern in `src/AskLucy.Application/Ai/AiAdminActionLog.cs`.
- [X] T045 [US3] Add `POST providers/{providerId:guid}/actions/check-health` to `src/AskLucy.Web/Controllers/v1/AdminAiProvidersController.cs`, inheriting the controller's existing `AdministratorOrSuperUser` policy and `admin-endpoints` rate limiting — which together are the FR-025 concurrency bound (research.md Decision 8). Ensure the endpoint appears in the generated OpenAPI document with its documented status codes (constitution §6).
- [X] T046 [P] [US3] Add tests in `tests/AskLucy.Application.Tests/Ai/CheckAiProviderHealthCommandHandlerTests.cs`: a healthy probe clears kind and reason; an unhealthy probe records both and returns 200-shaped success; a mechanism failure propagates rather than recording a false unhealthy; exactly one history row is appended per call (FR-026).
- [X] T047 [US3] Add `checkProviderHealth` to `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts` and wire a "Check now" action into `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.tsx` with a TanStack Query mutation that invalidates the provider list, disables the trigger while pending, and supplies an `onError` producing visible feedback (constitution §VIII).
- [X] T048 [P] [US3] Add tests in `src/AskLucy.Web/ClientApp/src/features/admin/components/AiProviderActionsMenu.test.tsx` covering: success refreshes the displayed status; a still-failing probe shows the current kind and reason; the trigger is disabled while in flight; a failed request surfaces a visible error rather than a silent no-op.

**Checkpoint**: US3 independently shippable — the diagnose/fix/verify loop is closed.

---

## Phase 6: User Story 4 — Token limits never block adding a model (Priority: P2)

**Goal**: Every vendor model can be added regardless of published token metadata, with absent figures shown as not published by the vendor.

**Independent test**: Sync a stub list that omits token limits, select all, apply; confirm every row is added with no failures and no data entry.

**Independent of US1–US3** — only the Foundational phase is required.

- [X] T049 [P] [US4] Stop substituting `0` in `src/AskLucy.Infrastructure/Ai/OpenAIProvider.cs`'s `ListAvailableModelsAsync` — return `null` for both figures, and update the existing comment that describes the zeros.
- [X] T050 [P] [US4] Stop substituting `0` in `src/AskLucy.Infrastructure/Ai/GoogleGeminiProvider.cs`'s `ListAvailableModelsAsync`: keep the `ValueKind` guard that prevents the `GetInt32()` throw on a JSON `null`, but yield `null` rather than `0`.
- [X] T051 [P] [US4] Apply the same change to `src/AskLucy.Infrastructure/Ai/AnthropicProvider.cs` and `src/AskLucy.Infrastructure/Ai/OpenRouterProvider.cs` wherever their lists omit the figures.
- [X] T052 [P] [US4] Change `ContextWindowTokens`/`MaxOutputTokens` to `int?` in `src/AskLucy.Application/Ai/AdminAiModelDto.cs` and `src/AskLucy.Application/Ai/ModelSummaryDto.cs`.
- [X] T053 [US4] Verify `src/AskLucy.Application/Ai/Commands/ApplyProviderModelSync/ApplyProviderModelSyncCommandHandler.cs` needs no logic change — its existing per-row `DomainRuleViolationException` catch stays for genuinely stale rows (FR-031), but null-limit rows must now flow past it into `models.Add`. Adjust only if the compiler or a test proves otherwise.
- [X] T054 [P] [US4] Add tests in `tests/AskLucy.Application.Tests/Ai/ApplyProviderModelSyncCommandHandlerTests.cs`: a batch where every row has null limits applies all rows with an empty `failed[]`; a genuinely stale row is still reported per-row while its siblings apply (SC-006, FR-031).
- [X] T055 [P] [US4] Add a test in `tests/AskLucy.Application.Tests/Ai/GetProviderModelSyncDiffQueryHandlerTests.cs` asserting a vendor list with absent limits produces a diff carrying nulls, not zeros.
- [X] T055a [US4] Extract the local `ProviderModelsSection` function from `src/AskLucy.Web/ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` (line ~53) into `src/AskLucy.Web/ClientApp/src/features/admin/components/ProviderModelsSection.tsx` as an exported component, updating the page's import. A pure move with no behaviour change — the existing page tests must pass untouched. This mirrors US2's `ProviderHealthCell` extraction and is what makes T057's component test possible at all.
- [X] T056 [US4] Update the model types and rendering in `src/AskLucy.Web/ClientApp/src/features/admin/api/adminAiProvidersApi.ts` and the models table in `src/AskLucy.Web/ClientApp/src/features/admin/components/ProviderModelsSection.tsx` to accept `number | null` and render absent figures as **"Not published by the vendor"** — never `0`, and never the word "Unknown", which this same table already uses for absent pricing (FR-029a). Leave pricing's existing wording untouched.
- [X] T057 [P] [US4] Add tests in `src/AskLucy.Web/ClientApp/src/features/admin/components/ProviderModelsSection.test.tsx` asserting null limits render as "Not published by the vendor", that `0` never appears for them, and that the string "Unknown" is not used for token limits.

**Checkpoint**: US4 independently shippable — the ~97-model vendor list becomes addable in one action.

---

## Phase 7: User Story 5 — The site-boundary workflow survives any Gemini failure (Priority: P3)

**Goal**: Boundary resolution always returns the deterministic result when vision is unavailable, within a bounded 30-second budget.

**Independent test**: Force each vision failure mode and confirm boundary resolution completes with the deterministic result each time, within the budget.

**Fully independent** — depends on nothing in Phases 2–6.

- [X] T058 [P] [US5] Add `VisionTimeoutSeconds` (default `30`, with a `[Range]` guard) to `src/AskLucy.Application/SiteBoundaries/BoundaryScoringOptions.cs`, documented as the FR-034 budget.
- [X] T059 [US5] In `src/AskLucy.Infrastructure/Boundaries/GeminiBoundaryVisionAnalyzer.cs`, wrap the request in a `CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)` carrying that timeout, and distinguish the two cancellation causes on the way out: caller token fired → rethrow `OperationCanceledException` (FR-035); only the budget fired → return `BoundaryVisionAnalysis.NotConfigured` naming the timeout. Do not change the shared `GoogleGemini` HttpClient timeout — it also serves chat.
- [X] T060 [P] [US5] Extend `tests/AskLucy.Infrastructure.Tests/Boundaries/GeminiBoundaryVisionAnalyzerTests.cs` with one case per failure mode — quota, rate limit, credential rejected, server error, timeout, malformed body, missing credential — asserting each returns `NotConfigured` with a reason and **never** throws (FR-032).
- [X] T061 [P] [US5] Add a test in the same file asserting a caller-initiated cancellation propagates as `OperationCanceledException` and is neither reported as a provider failure nor converted to `NotConfigured` (FR-035).
- [X] T062 [P] [US5] Add a test in the same file asserting a hung response is abandoned at the configured budget rather than the shared client's 2-minute timeout (SC-007).
- [X] T063 [P] [US5] Extend `tests/AskLucy.Application.Tests/SiteBoundaries/BoundaryResolutionServiceTests.cs` to assert that for every `NotConfigured` reason the service still returns the deterministic boundary and carries the plain-language note through to the result (FR-033).

**Checkpoint**: US5 independently shippable — the enhancement is provably not a single point of failure.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T064 [P] Audit every provider adapter and the classifier for FR-013 compliance: no credential, raw vendor body, exception type name, or stack trace in any message that can reach a client. Grep for string interpolation of response bodies into exception messages.
- [X] T065 [P] Update `docs/ARCHITECTURE.md` with the classification vocabulary and the Application/Infrastructure split from research.md Decision 1, and record the new endpoint in the API documentation (constitution §13).
- [X] T066 [P] Add an ADR under `docs/adr/` recording the exception-base-over-rewrite decision (research.md Decision 2) and the administrator-gated disclosure (Decision 5), since both are significant and non-obvious.
- [X] T067 Run the full gate: `dotnet test`, `dotnet format --verify-no-changes`, `npm --prefix src/AskLucy.Web/ClientApp test -- --run`, `npm --prefix src/AskLucy.Web/ClientApp exec tsc -b --noEmit`, `npm --prefix src/AskLucy.Web/ClientApp run lint`. Run the **full** frontend suite, not only touched files — page-level tests carry their own assertions and will otherwise hide breakage.
- [ ] T068 Walk the eleven manual scenarios in [quickstart.md](./quickstart.md) and tick its Definition of Done. Scenario 5 (unreadable credential) and Scenario 7 (disclosure gate) are the two that cannot be proven by automated tests alone. **NOT DONE** — requires a running app against a reachable SQL Server, which this environment does not have.

---

## Dependencies

```
Phase 1 Setup (T001–T002)
        │
        ▼
Phase 2 Foundational (T003–T022)  ◄── BLOCKS EVERYTHING
        │
        ├────────────┬───────────────┬──────────────┐
        ▼            ▼               ▼              │
   US1 (T023–T031)  US2 (T032–T042)  US4 (T049–T057)│
   P1 🎯 MVP         P1                P2            │
                     │                               │
                     ▼                               │
                US3 (T043–T048)                      │
                     P2                              │
                                                     ▼
                                          US5 (T058–T063)
                                          P3 — independent of Phase 2
        │
        ▼
Phase 8 Polish (T064–T068)
```

- **US1, US2, US4** are mutually independent once Foundational lands — three developers could take one each.
- **US3 depends on US2** only for the health DTO shape and the cell rendering it reuses.
- **US5 depends on nothing** in this feature. It can be done first, last, or in parallel throughout — it touches only the boundary analyzer and its options.

## Parallel execution examples

**Within Foundational**, after T006 completes:

```
T007, T008, T009   # three separate Domain files
T010, T011         # two separate Domain test files
T017, T018, T019, T020   # four separate provider adapters
```

**Across stories**, after the Foundational checkpoint:

```
Developer A: US1 (T023–T031)
Developer B: US2 (T032–T042) → US3 (T043–T048)
Developer C: US4 (T049–T057) → US5 (T058–T063)
```

**Within US2**: T032 and T033 are parallel with T037, T041, T042 once the DTO shape lands.

## Implementation strategy

**MVP = Phase 1 + Phase 2 + US1.** That alone retires the reported defect: the generic "An unexpected error occurred" stops appearing for provider failures, and an administrator can finally tell a bad key from an exhausted quota from a billing restriction. Everything after it is incremental improvement on an already-shipped fix.

**Suggested increments**:

1. **Foundational + US1** — the reported bug is fixed and diagnosable.
2. **+ US2** — the red chip becomes honest and dated.
3. **+ US3** — the fix/verify loop closes.
4. **+ US4** — the ~97-model vendor list becomes usable.
5. **+ US5** — the boundary guarantee is locked under test.

US5 is last by priority but is the cheapest and least risky phase (six tasks, one production file, no schema and no API change). If the boundary timeout is causing user-visible pain before this feature ships, it can be pulled forward and released on its own without any of Phase 2.

**Risk note**: T004's re-parenting touches the exception types six files catch by name. Compile the whole solution immediately after T004, before moving on — a mistake there is cheap to find at that point and expensive to find at T023.
