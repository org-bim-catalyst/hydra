# Implementation Plan: Password Recovery & Password Management

**Branch**: `058-password-recovery` | **Date**: 2026-09-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/058-password-recovery/spec.md`

## Summary

Close the only unrecoverable state in the sign-in journey: a user who forgets their password has no
self-service way back in. This adds a forgot-password request endpoint and a reset-link redemption
endpoint, backed by a new `PasswordResetToken` entity that stores only a hash of a single-use,
one-hour token; and it raises the existing change-password flow to the same standard (confirmation
field, per-rule policy feedback, session revocation, notification email, and a "set password" mode
for accounts created through an external provider).

The design choice that shapes everything else is using an owned, hashed, stored token rather than
ASP.NET Identity's stateless `DataProtectorTokenProvider` tokens — the stateless ones cannot be
superseded, are not single-use in their own right, and put account recovery on the Data Protection
key ring's survival, which this repository has already been burned by once. See
[research.md](./research.md) Topic 1.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend); TypeScript 5 / React 19 (frontend)

**Primary Dependencies**: ASP.NET Core, EF Core, ASP.NET Identity, MediatR, FluentValidation,
Serilog; React, Vite, MUI, TanStack Query, React Hook Form, Zod

**Storage**: SQL Server — one new table, `PasswordResetTokens`, one migration, no backfill

**Testing**: xUnit + NSubstitute + FluentAssertions (Application/Domain unit, Web integration via
`CustomWebApplicationFactory`); Vitest + Testing Library (frontend)

**Target Platform**: ASP.NET Core on Windows/IIS (site4now shared hosting today), browser SPA

**Project Type**: Web application — Clean Architecture backend with a colocated React client

**Performance Goals**: The forgot-password endpoint enqueues the email and responds in under 500 ms
at p95, identically whether or not the address has an account (SC-002 — this half is build-testable
and doubles as the FR-003 timing assertion). End-to-end delivery within 60s for 95% of requests is
an operational SLO, not a build gate, since it is a property of the mail provider. No new query on
any hot path — all new reads are per-request on auth endpoints only

**Constraints**: Forgot-password responses must not reveal account existence by status code, body
or coarse timing (FR-003); token material must never reach logs or storage in usable form (FR-016);
every failure path must produce visible UI feedback (FR-017)

**Scale/Scope**: 4 endpoints (2 new, 1 extended, 1 trivial query), 1 entity, 1 migration,
2 new pages, 1 reworked settings section, 2 email messages

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Assessment | Verdict |
|-----------|-----------|---------|
| §2.I Clean Architecture & Dependency Rule | `PasswordResetToken` in Domain; commands, validators and the new `IPasswordResetTokenRepository` in Application; EF configuration, repository implementation and Identity access in Persistence; controller in Web. No outward-to-inward arrow. Application gains no EF Core reference — the repository is an Application-owned interface, per the standing note that Application never references EF Core | PASS |
| §2.II SOLID | Each command is one handler with one reason to change; the repository interface is consumed, not the DbContext | PASS |
| §2.III Simplicity / YAGNI | One entity, no templating engine, no new background job beyond the existing cleanup infrastructure. The alternative designs rejected in research.md were all *more* machinery | PASS |
| §2.V Dependency Inversion & Testability | Every collaborator (the email job abstraction, `ITokenService`, `IIdentityService`, both repositories) is injected, and `PasswordResetToken.IssueNew` takes an explicit lifetime like its `RefreshToken` sibling — so expiry, supersession and single-use are all unit-testable without a host or a clock abstraction | PASS |
| §2.VIII No Silent Failures (NON-NEGOTIABLE) | Every backend failure returns Problem Details or is logged with cause; every frontend call has an explicit error path rendering an `Alert` or field error. **One deliberate asymmetry**: the forgot-password endpoint returns 202 even when the account does not exist or the enqueued email dispatch later fails. The failure is fully captured and diagnosable in the log — it is simply not shown to the caller, because FR-003 forbids it. This is the "capture, not expose" reading of §VIII, not an exception to it | PASS |
| §5 Database Principles | Code-first migration, UTC timestamps, indexed FK, sequential GUID key, audit rows retained | PASS |
| §6 API Standards | Versioned route, Problem Details, validated input, **rate limiting added** — this feature also closes a standing gap by introducing the first `auth-endpoints` policy, since `/auth/*` had none | PASS |
| §7 UI Principles | Both new pages follow the existing auth-page layout, keyboard-navigable, labelled for screen readers, light and dark themes (FR-018) | PASS |
| §8 Security | Token stored hashed only; single-use; time-limited; superseded on reissue; bound to the email at issue; sessions revoked on change; security events recorded; no enumeration oracle; 2FA not bypassable | PASS |
| §10 Testing Standards | Unit tests for token state transitions and each handler; integration tests for the four endpoints including reuse/expiry/supersession/tamper; frontend tests for both pages and the settings form | PASS |
| §13 Documentation | Architecture, API and database docs updated as part of the implementation, plus an ADR for the Topic 1 decision | PASS |

**Result**: No violations. One item in Complexity Tracking is a scope boundary, not a violation.

## Project Structure

### Documentation (this feature)

```text
specs/058-password-recovery/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── password-api.md  # Phase 1 output
├── checklists/
│   └── requirements.md  # From /speckit-specify
└── tasks.md             # NOT created by /speckit-plan
```

### Source Code (repository root)

```text
src/
├── AskLucy.Domain/
│   └── Authentication/
│       └── PasswordResetToken.cs                      # NEW
├── AskLucy.Application/
│   ├── Abstractions/
│   │   ├── IPasswordResetTokenRepository.cs           # NEW
│   │   ├── IPasswordEmailJob.cs                       # NEW — Hangfire-backed, keeps SMTP off the request path
│   │   ├── IRefreshTokenRepository.cs                 # + ListActiveByUserAsync
│   │   └── IIdentityService.cs                        # + ResetPasswordAsync, SetPasswordAsync
│   └── Authentication/Commands/
│       ├── RequestPasswordReset/                      # NEW command + handler + validator
│       ├── ResetPassword/                             # NEW command + handler + validator
│       └── ChangePassword/                            # extended: revoke sessions, notify
├── AskLucy.Persistence/
│   ├── Identity/IdentityService.cs                    # + reset/set password
│   ├── Configurations/PasswordResetTokenConfiguration.cs   # NEW
│   ├── Repositories/PasswordResetTokenRepository.cs        # NEW
│   └── Migrations/…_AddPasswordResetTokens.cs              # NEW
├── AskLucy.Infrastructure/
│   └── Email/
│       ├── PasswordEmailJob.cs                        # NEW — Hangfire implementation of IPasswordEmailJob
│       └── PasswordTokenProtector.cs                  # NEW — IDataProtector wrapper; the token is ciphertext in the job store
└── AskLucy.Web/
    ├── Program.cs                                     # + "auth-endpoints" rate-limit policy
    ├── Contracts/AuthContracts.cs                     # + request records
    ├── Controllers/v1/AuthController.cs               # + 3 endpoints, 1 extended
    └── ClientApp/src/
        ├── routes/router.tsx                          # + /forgot-password, /reset-password
        └── features/
            ├── auth/
            │   ├── api/authApi.ts                     # + 3 functions
            │   ├── hooks/usePasswordReset.ts          # NEW
            │   └── pages/
            │       ├── ForgotPasswordPage.tsx         # NEW (+ test, + a11y test)
            │       ├── ResetPasswordPage.tsx          # NEW (+ test, + a11y test)
            │       └── LoginPage.tsx                  # + "Forgot password?" link
            └── settings/pages/SettingsPage.tsx        # reworked password section

tests/
├── AskLucy.Application.Tests/Authentication/          # handler + token lifecycle unit tests
├── AskLucy.Web.Tests/Authentication/                  # endpoint integration tests
└── (frontend tests colocated beside their components, per repo convention)
```

**Structure Decision**: The existing four-project Clean Architecture backend with the React client
under `src/AskLucy.Web/ClientApp`. This feature adds no project and no new architectural seam — it
extends the authentication vertical that already spans all four layers.

## Complexity Tracking

> Recorded for the reviewer's benefit. Neither row is a constitution violation.

| Item | Why it is this way | Alternative rejected because |
|------|--------------------|------------------------------|
| Access tokens stay valid until natural expiry after a password change; only refresh tokens are revoked | Sessions are refresh-token families, and revoking them is what "sign out my other devices" means in this codebase's existing model | Per-request Identity security-stamp validation would close the residual access-token window, but it changes the JWT pipeline for every endpoint in the product. That is a platform-wide change deserving its own spec, not a rider on this one |
| A second, application-level per-email throttle sits alongside the ASP.NET rate limiter | The framework limiter can only reject with a status code, and a 429 keyed on an email address is itself an account-existence oracle (FR-003) | Using only the framework limiter would leak enumeration; using only the application throttle would leave the endpoint without the §6-mandated policy |
