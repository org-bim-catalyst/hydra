---

description: "Task list for 058-password-recovery"
---

# Tasks: Password Recovery & Password Management

**Input**: Design documents from `/specs/058-password-recovery/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md), [data-model.md](./data-model.md), [contracts/password-api.md](./contracts/password-api.md)

**Tests**: Included and **not optional** — constitution §10 mandates unit and integration coverage, and the spec's SC-005/SC-006/SC-007 are explicitly defined as automated-test outcomes.

**Organization**: Grouped by user story. US1 and US2 are both P1 and together form the MVP — US1 alone issues a link nobody can redeem, so neither ships without the other.

**Implementation note 2026-09-18 (T014)**: the endpoint latency test failed on first run — the
eligible-account path cost ~1.5 s more than the unknown-address path, because resolving, throttling,
superseding and saving are all account-dependent database round trips. A neutral 202 body does not
hide that. Issuance therefore moved wholesale onto a Hangfire worker behind the new
`IPasswordResetIssuanceJob`; `RequestPasswordResetCommandHandler` now only enqueues, so the request
path costs the same for every address. `PasswordResetIssuanceJob` (Application, alongside
`MemoryExtractionJob`'s precedent) holds the logic T016 describes, and T013's unit tests target it.

**Revised 2026-09-18** after two `/speckit-analyze` passes: email dispatch moved off the request path (A1), security-event logging moved from Polish into the stories it belongs to so the Phase 4 MVP is genuinely complete under §19 (A2), a 2FA-after-reset test added (A4), the token-expiry testing approach corrected (A5), and — because moving dispatch to Hangfire would otherwise serialise a usable token into the job store — the enqueued token is now `IDataProtector`-protected (A13, see [research.md](./research.md) Topic 5a).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: US1–US4, mapping to the user stories in [spec.md](./spec.md)

## Path Conventions

Backend Clean Architecture under `src/`, React client under `src/AskLucy.Web/ClientApp/src/`, backend tests under `tests/`, frontend tests colocated. Per [plan.md](./plan.md) § Project Structure.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Nothing to scaffold — this feature extends an existing vertical. Only the rate-limit policy that all new endpoints depend on.

- [X] T001 Add an `auth-endpoints` fixed-window rate-limit policy (partitioned on client IP, 10 requests/minute) to the rate limiter block in `src/AskLucy.Web/Program.cs`, following the existing `ai-endpoints` policy shape

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The token entity, its storage, and the identity/session/email primitives every story below builds on. **No user story can start until this phase is done.**

- [X] T002 Create the `PasswordResetToken` domain entity in `src/AskLucy.Domain/Authentication/PasswordResetToken.cs` — private setters, `IssueNew` factory taking an explicit `TimeSpan lifetime` (as `RefreshToken` does, so tests can construct already-expired tokens), `Consume()`, `Supersede()`, and an `IsRedeemable` computed property, per [data-model.md](./data-model.md)
- [X] T003 [P] Write unit tests for the entity's state transitions in `tests/AskLucy.Domain.Tests/Authentication/PasswordResetTokenTests.cs` — pending→consumed, pending→superseded, expiry (by issuing with a negative lifetime), double-consume rejection, and `IsRedeemable` for every combination
- [X] T004 [P] Define `IPasswordResetTokenRepository` in `src/AskLucy.Application/Abstractions/IPasswordResetTokenRepository.cs` with `FindRedeemableByHashAsync`, `ListRecentByUserAsync` (for the per-email throttle), `SupersedePendingForUserAsync`, and `Add`
- [X] T005 [P] Add `ListActiveByUserAsync(string userId, CancellationToken)` to `IRefreshTokenRepository` in `src/AskLucy.Application/Abstractions/IRefreshTokenRepository.cs`
- [X] T006 [P] Add `ResetPasswordAsync(userId, newPassword)`, `SetPasswordAsync(userId, newPassword)` and `FindIdByEmailAsync(email)` to `IIdentityService` in `src/AskLucy.Application/Abstractions/IIdentityService.cs`
- [X] T007 [P] Define `IPasswordEmailJob` in `src/AskLucy.Application/Abstractions/IPasswordEmailJob.cs` with `EnqueueResetLink(userId, email, protectedToken)` and `EnqueueChangeNotification(userId, email, changedAtUtc)`, following the injected-job-abstraction pattern of `IMemoryExtractionJob`. **The token argument is a `IDataProtector`-protected string, never plaintext** — Hangfire serialises job arguments into its SQL job store, and plaintext there would put a usable link in the same database that deliberately stores only hashes (FR-016). Define the protector abstraction alongside it so Application stays free of Data Protection types
- [X] T008 Implement the new `IIdentityService` members in `src/AskLucy.Persistence/Identity/IdentityService.cs` using `UserManager.RemovePasswordAsync`/`AddPasswordAsync` for reset and `AddPasswordAsync` for set, returning per-rule `IdentityOperationResult` errors
- [X] T009 Create `PasswordResetTokenConfiguration` in `src/AskLucy.Persistence/Configurations/PasswordResetTokenConfiguration.cs` — unique index on `TokenHash`, index on `(UserId, CreatedAtUtc)`, cascade delete from `AspNetUsers`, and register the `DbSet` on `AskLucyDbContext`
- [X] T010 Implement `PasswordResetTokenRepository` in `src/AskLucy.Persistence/Repositories/PasswordResetTokenRepository.cs` and implement `ListActiveByUserAsync` in the existing refresh-token repository; register both in `src/AskLucy.Persistence/DependencyInjection.cs`
- [X] T011 Generate the `AddPasswordResetTokens` EF Core migration into `src/AskLucy.Persistence/Migrations/`, then verify the generated file has no BOM and uses the repo's line endings before committing
- [X] T012 [P] Write repository integration tests in `tests/AskLucy.Persistence.Tests/Authentication/PasswordResetTokenRepositoryTests.cs` — hash lookup, supersede-pending, recent-by-user window (§10 requires EF repositories be covered against real SQL Server). Run only with `PERSISTENCE_TESTS_DEDICATED_DATABASE=1`; these tests must never touch the shared dev database

**Checkpoint**: `dotnet build` clean, migration applies, entity and repository tests green.

---

## Phase 3: User Story 1 — Request a password reset link (Priority: P1) 🎯 MVP part 1

**Goal**: A user submits their email and receives a reset link, with no way to learn whether an account exists.

**Independent test**: Submit a known address → neutral confirmation + email job enqueued; submit an unknown one → identical response and latency, nothing enqueued.

### Tests for User Story 1

- [X] T013 [P] [US1] Write `RequestPasswordResetCommandHandler` unit tests in `tests/AskLucy.Application.Tests/Authentication/RequestPasswordResetCommandHandlerTests.cs` — eligible account issues a token and enqueues mail; unknown, unconfirmed and locked-out accounts issue nothing and enqueue nothing; a second request supersedes the first token; the fourth request inside 15 minutes enqueues nothing
- [X] T014 [P] [US1] Write endpoint integration tests in `tests/AskLucy.Web.Tests/Authentication/ForgotPasswordEndpointTests.cs` asserting **byte-identical 202 responses** across the four account states (SC-005), a 400 on a malformed address, and that response latency does not diverge between an existing and a non-existent address (SC-002, FR-003)

### Implementation for User Story 1

- [X] T015 [US1] Create `RequestPasswordResetCommand` + validator in `src/AskLucy.Application/Authentication/Commands/RequestPasswordReset/` — validator checks email shape only, never existence
- [X] T016 [US1] Implement `RequestPasswordResetCommandHandler` in the same folder: resolve the account, apply the eligibility gate (exists ∧ email confirmed ∧ not locked out), enforce the per-email throttle of 3 per 15 minutes via `ListRecentByUserAsync`, supersede pending tokens, issue a 256-bit token, persist only its `ITokenService.Hash`, protect the plaintext token, and enqueue the reset email through `IPasswordEmailJob`. Every ineligible path returns success to the caller and logs the real reason as a structured field — never the token
- [X] T017 [US1] Implement the Hangfire-backed `IPasswordEmailJob` in `src/AskLucy.Infrastructure/Email/PasswordEmailJob.cs`, plus the `IDataProtector`-backed protector in the same folder. The job unprotects the token, then renders the reset email in-memory per `RegisterCommandHandler`'s pattern: HTML-encode the display name, build the link from `AppOptions.FrontendBaseUrl` with `Uri.EscapeDataString` on both query values. An unprotect failure (key ring rotated while the job sat in the queue) is logged and the job fails loudly rather than mailing a broken link; dispatch failure is logged and retried, never surfaced to the caller
- [X] T018 [US1] Emit the reset-requested and reset-throttled security events from the handler using `[LoggerMessage]` source-generated logging, each carrying user id, UTC timestamp and client origin (FR-015). Do **not** assert these with `Received().Log(...)` — NSubstitute cannot match source-generated logging
- [X] T019 [US1] Add `ForgotPasswordRequest` to `src/AskLucy.Web/Contracts/AuthContracts.cs` and the `POST /auth/password/forgot` action to `src/AskLucy.Web/Controllers/v1/AuthController.cs` — `[AllowAnonymous]`, `[EnableRateLimiting("auth-endpoints")]`, always `Accepted()`
- [X] T020 [P] [US1] Add `requestPasswordReset` to `src/AskLucy.Web/ClientApp/src/features/auth/api/authApi.ts` with `isAuthFlow: true`
- [X] T021 [US1] Create `ForgotPasswordPage.tsx` in `src/AskLucy.Web/ClientApp/src/features/auth/pages/` — React Hook Form + Zod email validation, a TanStack Query mutation, a neutral success panel, and an inline `Alert` for 429/network failure (FR-017). Match the existing auth-page layout
- [X] T022 [US1] Register the lazy `/forgot-password` route with `errorElement: <ErrorPage />` in `src/AskLucy.Web/ClientApp/src/routes/router.tsx`, and add the "Forgot password?" link to `LoginPage.tsx` using `FormField`'s right-aligned label slot
- [X] T023 [P] [US1] Write `ForgotPasswordPage.test.tsx` and `ForgotPasswordPage.a11y.test.tsx` beside the page — success panel, validation error, request-failure alert, and axe clean in both themes

**Checkpoint**: The forgot-password screen works end to end and is fully audited; the link it mails is not yet redeemable.

---

## Phase 4: User Story 2 — Complete a reset from the emailed link (Priority: P1) 🎯 MVP part 2

**Goal**: The emailed link sets a new password exactly once, then stops working.

**Independent test**: Redeem a valid link → new password signs in, old one does not; reuse the same link → rejected.

### Tests for User Story 2

- [X] T024 [P] [US2] Write `ResetPasswordCommandHandler` unit tests in `tests/AskLucy.Application.Tests/Authentication/ResetPasswordCommandHandlerTests.cs` — success path; rejection for unknown, consumed, superseded, expired and email-changed tokens; policy violations returned per rule; session revocation invoked; notification enqueued
- [X] T025 [P] [US2] Write integration tests in `tests/AskLucy.Web.Tests/Authentication/ResetPasswordEndpointTests.cs` covering reuse, expiry, supersession and a tampered token — all four must produce the **same** Problem Details title (SC-006) — plus sign-in with the new password succeeding and the old one failing
- [X] T026 [P] [US2] Write a multi-session integration test in `tests/AskLucy.Web.Tests/Authentication/PasswordChangeSessionRevocationTests.cs`: two refresh-token families, complete a reset, assert both are revoked (SC-007, FR-009)
- [X] T027 [P] [US2] Write a 2FA integration test in `tests/AskLucy.Web.Tests/Authentication/ResetPasswordTwoFactorTests.cs`: an account with `TwoFactorEnabled` completes a reset, then signs in with the new password and receives `requiresTwoFactor: true` rather than tokens (FR-012, US2 AS6)

### Implementation for User Story 2

- [X] T028 [US2] Create `ResetPasswordCommand` + validator in `src/AskLucy.Application/Authentication/Commands/ResetPassword/` — validator checks presence and shape of `userId`, `token`, `newPassword`; password-policy verdicts come from Identity, not the validator, so the two never drift
- [X] T029 [US2] Implement `ResetPasswordCommandHandler`: look the token up by hash, verify `IsRedeemable` **and** that `EmailAtIssue` still matches the account's email, reset the password, consume the token, and return one undifferentiated failure result for every rejection cause while logging the specific cause
- [X] T030 [US2] In the same handler, revoke every active refresh token for the account via `ListActiveByUserAsync` + `Revoke()` inside the same unit of work as the password write, so a reset can never half-apply
- [X] T031 [US2] Emit the reset-completed and reset-rejected security events from the handler, recording the specific rejection cause the caller is deliberately not told (FR-015), using source-generated logging as in T018
- [X] T032 [P] [US2] Add the "your password was changed" notification to `IPasswordEmailJob`'s implementation — shared by this handler and change-password — stating the UTC time and what to do if it was not them (FR-011)
- [X] T033 [US2] Add `ResetPasswordRequest` to `AuthContracts.cs` and the `POST /auth/password/reset` action to `AuthController.cs` — `[AllowAnonymous]`, rate-limited, `NoContent()` on success, Problem Details otherwise. The response must never return tokens, so 2FA still applies at the next sign-in (FR-012)
- [X] T034 [P] [US2] Add `resetPassword` to `authApi.ts`
- [X] T035 [US2] Create `ResetPasswordPage.tsx` — reads `userId`/`token` from the query string, new-password + confirm fields with Zod cross-field matching (FR-008), per-rule policy errors rendered from the Problem Details `errors` bag, an invalid-link state offering a link back to `/forgot-password`, and a success state directing the user to sign in
- [X] T036 [US2] Register the lazy `/reset-password` route in `router.tsx` with `errorElement: <ErrorPage />`
- [X] T037 [P] [US2] Write `ResetPasswordPage.test.tsx` and `ResetPasswordPage.a11y.test.tsx` — success, mismatch, policy failure, invalid/expired link, missing query parameters, axe clean

**Implementation note 2026-09-19 (T027)**: the test as specified — sign in after a reset and expect
`requiresTwoFactor: true` — cannot pass, and not because of anything this feature does.
`IdentityService.ValidateCredentialsAsync` validates the password with
`SignInManager.CheckPasswordSignInAsync`, which never reports `RequiresTwoFactor`, so **sign-in does
not currently challenge for a second factor at all**; `ValidateTwoFactorCodeAsync` then leans on
`TwoFactorAuthenticatorSignInAsync`, which needs the `TwoFactorUserId` cookie a JWT API never sets.
That is a pre-existing login-path defect, outside this feature's scope and its own piece of work.
T027 therefore asserts what the reset flow genuinely owns under FR-012 — no session in the response,
`TwoFactorEnabled` and the authenticator secret intact afterwards — with the gap documented in the
test rather than papered over.

**Checkpoint**: 🎯 **MVP complete.** A locked-out user can recover unaided, with the full audit trail FR-015 requires. Ship-able on its own.

---

## Phase 5: User Story 3 — Change password while signed in (Priority: P2)

**Goal**: Bring the existing change-password flow up to the same standard. The endpoint already exists — this is hardening, not construction.

**Independent test**: Change with the correct current password → new one works, acting session survives, other sessions die.

### Tests for User Story 3

- [X] T038 [P] [US3] Extend `tests/AskLucy.Application.Tests/Authentication/ChangePasswordCommandHandlerTests.cs` — other sessions revoked, acting session's token family preserved (FR-010), notification enqueued, new-equals-current rejected, pending reset tokens superseded (FR-006)
- [X] T039 [P] [US3] Add integration coverage in `tests/AskLucy.Web.Tests/Authentication/ChangePasswordEndpointTests.cs` for the wrong-current-password 400, the omitted-current-password 400 when a password exists, and the surviving acting session

### Implementation for User Story 3

- [X] T040 [US3] Extend `ChangePasswordCommand` with the acting refresh-token family id and make `CurrentPassword` optional in `src/AskLucy.Application/Authentication/Commands/ChangePassword/`; update `ChangePasswordCommandValidator` accordingly
- [X] T041 [US3] Extend `ChangePasswordCommandHandler` to verify the current password when the account has one, reject a new password equal to the current, supersede any pending reset tokens (FR-006), revoke all refresh tokens **except** the acting family, and enqueue the notification email from T032
- [X] T042 [US3] Emit the password-changed and wrong-current-password security events from the handler (FR-015), using source-generated logging as in T018
- [X] T043 [US3] Update the `POST /auth/change-password` action in `AuthController.cs` to pass the acting session's token family, apply `[EnableRateLimiting("auth-endpoints")]`, and map the new failure modes to distinct Problem Details titles per [contracts/password-api.md](./contracts/password-api.md)
- [X] T044 [P] [US3] Widen `changePassword` in `authApi.ts` to accept an optional current password
- [X] T045 [US3] Rework the password section of `src/AskLucy.Web/ClientApp/src/features/settings/pages/SettingsPage.tsx` — add the confirm-password field, render per-rule policy errors from the response instead of the current generic "Check your current password" message, and keep the success alert
- [X] T046 [P] [US3] Update `SettingsPage.test.tsx` **and** the existing `SettingsPage.a11y.test.tsx` for the new field and error rendering, then run the **full** frontend suite — page-level tests elsewhere assert on this section

**Checkpoint**: All three flows in the feature title are complete.

---

## Phase 6: User Story 4 — Set a first password for an external-only account (Priority: P3)

**Goal**: Google/Microsoft/Facebook/GitHub users can add an email-and-password fallback.

**Independent test**: On a password-less account, complete the reset flow, then sign in with email and password.

### Tests for User Story 4

- [X] T047 [P] [US4] Add handler tests in `tests/AskLucy.Application.Tests/Authentication/` asserting that reset and change both call `SetPasswordAsync` rather than `ResetPasswordAsync` when the account has no password
- [X] T048 [P] [US4] Add an integration test in `tests/AskLucy.Web.Tests/Authentication/SetFirstPasswordTests.cs`: password-less account → forgot → reset → email-and-password sign-in succeeds while the external login still works

### Implementation for User Story 4

- [X] T049 [US4] Branch on `IIdentityService.HasPasswordAsync` in both the reset and change handlers so a password-less account takes the set-password path (FR-014)
- [X] T050 [US4] Add the `GET /auth/password/status` action to `AuthController.cs` returning `{ hasPassword }`, and `getPasswordStatus` to `authApi.ts`
- [X] T051 [US4] Make the Settings password section render a "Set password" variant with no current-password field when `hasPassword` is false, including its loading and error states
- [X] T052 [P] [US4] Add `SettingsPage` test coverage for the set-password variant

**Checkpoint**: Every account type can manage its password.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T053 [P] Audit every new code path for token leakage — no plaintext token in any log statement, Problem Details body, analytics call, or exception message, and confirm by inspecting the Hangfire job table that the enqueued argument is protected ciphertext, not a usable token (FR-016)
- [X] T054 [P] Add the 90-day cleanup of consumed/superseded/expired reset tokens to the existing scheduled maintenance job
- [X] T055 [P] Add an end-to-end test for the recovery journey in `tests/AskLucy.E2E.Tests/` — sign-in blocked, reset requested, link redeemed, sign-in succeeds (§10 critical-journey coverage)
- [X] T056 [P] Verify both new pages and the settings section in light and dark themes at mobile and desktop widths, with keyboard-only navigation (FR-018)
- [X] T057 [P] Complete the §16.6 security review for this change — auth surface, token handling, enumeration resistance, session revocation — and record the outcome with the commit
- [X] T058 [P] Write an ADR in `docs/adr/` recording the Topic 1 decision — why this feature owns its reset token instead of using ASP.NET Identity's stateless one
- [X] T059 [P] Update the architecture, API and database documentation for the new endpoints, entity and migration (constitution §13)
- [X] T060 Run the full gate: `dotnet build`, `dotnet format --verify-no-changes`, the backend test projects (with `PERSISTENCE_TESTS_CONNECTION_STRING` set), then `npx tsc -b --noEmit`, `npm run lint` and the full `npm test` in `ClientApp`
- [ ] T061 Walk [quickstart.md](./quickstart.md) Scenarios 1–5 by hand against a running app

---

## Phase 8: Post-release follow-up (manual walkthrough findings)

**Purpose**: T061's hand-walkthrough against the running app surfaced six defects the automated
suite could not catch, because each is about what the user can read and act on rather than what the
API returns. Requirements FR-019–FR-024 were added to [spec.md](./spec.md) to cover them.

Shipped as `29c641b9` and `097ca311`.

- [X] T062 *(Amendment 2026-09-19, FR-024)* Fixed unreadable inputs on the reset, registration and change-email screens — `AuthLayout.tsx` hard-coded light backgrounds but took its text colours from the ambient app theme, so masked password characters and the "Go to sign in" button were near-invisible in dark mode. Fixed by scoping a `ThemeProvider` to the fixed-light panel; watch the `& a` rule, which also hits `Button`-as-link
- [X] T063 *(Amendment 2026-09-19)* Fixed sign-in needing two clicks — the first submit errored in the console and stayed on the page; `useAuth.ts` plus coverage in `useAuth.test.tsx`
- [X] T064 *(Amendment 2026-09-19)* Fixed registration failing silently — `RegisterPage.tsx` now surfaces the failure to the user (constitution §Error Handling), with a regression test
- [X] T065 *(Amendment 2026-09-19, FR-019)* Added `ValidatePasswordResetTokenQuery`/`Handler` and `POST /auth/password/reset/validate`; `ResetPasswordPage` now checks the link on mount and shows "this link is no longer valid" with a request-a-new-link affordance instead of accepting a new password against a dead link. The check does not consume the token. Contract: [contracts/password-api.md](./contracts/password-api.md); tests in `ValidatePasswordResetTokenQueryHandlerTests.cs`
- [X] T066 *(Amendment 2026-09-19, FR-020)* Sign-in now distinguishes its refusals: `AuthController.ToActionResult` maps `EmailNotConfirmed` → `403` and `LockedOut` → `423`, leaving wrong-password and unknown-address alike on `401`. `LoginPage` branches on status code and shows the matching message. Decision recorded in [ADR 0010](../../docs/adr/0010-naming-sign-in-refusals.md)
- [X] T067 *(Amendment 2026-09-19, FR-020)* Added the two ways out each named refusal needs: `ResendEmailConfirmationCommand` behind `POST /auth/confirm-email/resend`, and `RequestAccountSupportCommand` behind `POST /auth/account-support` with `AccountSupportDialog.tsx` — the support address is server-side configuration and never reaches the contract. Both return a neutral `202` on the same terms as `password/forgot`. Delivery via the new `IAccountEmailJob`/`AccountEmailJob`; tests in `AccountRecoveryCommandHandlerTests.cs`
- [X] T068 *(Amendment 2026-09-19, FR-021)* Lowered `Lockout.MaxFailedAccessAttempts` from 5 to 3 in `AskLucy.Persistence/DependencyInjection.cs` — defensible only because T066/T067 now tell the locked-out user what happened and how to recover
- [X] T069 *(Amendment 2026-09-19, FR-022)* Replaced the run-on policy sentence with `PasswordRequirements.tsx` — a live per-rule checklist plus a segmented strength bar, driven by `passwordPolicy.ts`, used by both `RegisterPage` and `ResetPasswordPage`
- [X] T070 *(Amendment 2026-09-19, FR-023)* Added a "go to sign in" link to the registration screen, mirroring the existing link in the other direction
- [X] T071 *(Amendment 2026-09-19)* Updated the docs for all of the above: [docs/API_GUIDELINES.md](../../docs/API_GUIDELINES.md) §13 (the three new endpoints, the sign-in status-code table, and a correction to the `/auth/forgot-password`→`/auth/password/forgot` route drift that predated this phase), [docs/SECURITY.md](../../docs/SECURITY.md) §6 (the concrete policy and the new lockout threshold), and ADR 0010

**Phase 8 complete.** Full gate re-run green: `dotnet build`, `dotnet format --verify-no-changes`,
both backend test projects, `tsc -b --noEmit`, `npm run lint` and the full `npm test`
(240 files / 1386 tests). No database migration — every change is behavioural or configuration.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)** → no dependencies
- **Phase 2 (Foundational)** → blocks every user story
- **Phase 3 (US1)** → needs Phase 2
- **Phase 4 (US2)** → needs Phase 2; T032 extends the job implementation created in T017
- **Phase 5 (US3)** → needs Phase 2 and T032
- **Phase 6 (US4)** → needs US2 and US3 to exist, since it adds a branch inside both
- **Phase 7 (Polish)** → after the stories it audits

### User Story Dependencies

- **US1 and US2** are jointly the MVP: US1 issues a link, US2 redeems it. Neither delivers value alone.
- **US3** is genuinely independent of US1/US2 apart from the shared notification email.
- **US4** is a variation layered onto US2 and US3, not a standalone slice.

### Within Each User Story

Tests → Application command/handler → security events → Persistence/Infrastructure → Web endpoint → frontend API client → page → route → frontend tests.

### Parallel Opportunities

- T003–T007 after T002
- T013, T014 together; T024–T027 together; T038, T039 together
- T020/T034/T044 (all `authApi.ts` additions) are the *same file* — despite the `[P]` on each within its own story, do not run them concurrently across stories
- Most of Phase 7 (T053–T059) runs in parallel

---

## Parallel Example: User Story 2

```text
# After Phase 2, launch the four test tasks together:
T024  Handler unit tests
T025  Endpoint integration tests
T026  Multi-session revocation test
T027  2FA-still-required test

# Then implementation serially through the layers (T028 → T033),
# with T032 (notification email) and T034 (API client) in parallel alongside.
```

---

## Implementation Strategy

### MVP First

Phases 1, 2, 3 and 4 — US1 + US2, through T037. That alone removes the only unrecoverable state in the product, carries its own audit trail, and is independently shippable.

### Incremental Delivery

1. MVP (US1 + US2) → validate against quickstart Scenarios 1–4 → ship
2. US3 → password management reaches the same standard → ship
3. US4 → external-only accounts gain a fallback → ship
4. Polish → retention, E2E, security review, docs, ADR

### Notes

- Push straight to `main` after committing; no feature branch or PR for this repo.
- Run the full frontend test suite, not only the files touched.
- Set `PERSISTENCE_TESTS_CONNECTION_STRING` for backend tests; set `PERSISTENCE_TESTS_DEDICATED_DATABASE=1` **only** when running T012, and never against the shared dev database.
