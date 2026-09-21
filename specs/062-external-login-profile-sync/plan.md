# Implementation Plan: External Login Profile Sync

**Branch**: `062-external-login-profile-sync` | **Date**: 2026-09-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/062-external-login-profile-sync/spec.md`

## Summary

Google and Facebook already send first name, last name, and a profile-picture URL as OAuth claims on every sign-in, but Ask Lucy's external-login handler today only extracts `NameIdentifier` and `Email`/`email_verified`. This feature (1) maps the missing claims in the Google/Facebook handler configuration, (2) threads them through the existing external-login command pipeline into `ApplicationUser.FirstName`/`LastName`/`AvatarFileName` — which already exist and are already displayed — refreshing them on every sign-in and every provider-link action rather than only at account creation, and (3) fetches/stores the picture asynchronously via a Hangfire background job (mirroring the existing `IPasswordEmailJob` pattern) so a slow or failing image fetch never adds latency to, or can fail, the sign-in itself. No new database schema, no new public API endpoint, and no frontend changes are required — `GET /api/v1/users/me` already returns these fields.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (backend-only feature; no frontend changes — `ClientApp`'s existing `/users/me` consumer already renders `firstName`/`lastName`/avatar)

**Primary Dependencies**: ASP.NET Core external authentication (`Microsoft.AspNetCore.Authentication.Google`/`.Facebook`), MediatR, Hangfire (`IBackgroundJobClient`, already used by `IPasswordEmailJob`/`IPasswordResetIssuanceJob`), EF Core (`ApplicationUser`), existing `IFileStorage` and `IRemoteFileDownloader` abstractions

**Storage**: SQL Server via EF Core — reuses existing `ApplicationUser.FirstName`, `LastName`, `AvatarFileName` columns; no migration

**Testing**: xUnit + NSubstitute + FluentAssertions, matching `AskLucy.Application.Tests`, `AskLucy.Infrastructure.Tests`, and `AskLucy.Web.Tests` conventions already used for the external-login feature area

**Target Platform**: Existing ASP.NET Core Web API (`AskLucy.Web`), server-side only

**Project Type**: Web application (existing backend + frontend monorepo) — this feature is backend-only

**Performance Goals**: Zero added latency to the sign-in/link request path from picture fetch/store (async, per Clarifications); name/last-name sync adds one additional repository round trip already on the request path

**Constraints**: No new DB schema; must not block or fail sign-in on an external HTTP call or malformed claim (constitution §8, spec FR-005/FR-003b); picture download capped at the same 5MB limit already enforced for manually uploaded avatars (`UsersController.UploadAvatar`'s `[RequestSizeLimit(5 * 1024 * 1024)]`); outbound picture fetch restricted to the providers' known image-CDN hosts to avoid turning a third-party-supplied URL claim into an open server-side fetch (SSRF hardening, constitution §8 OWASP baseline)

**Scale/Scope**: Applies uniformly to every Google/Facebook sign-in and every "link an additional provider" action, for all existing and future users — no batch backfill job, no admin tooling

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Clean Architecture & Dependency Rule** — PASS. New claim data flows outward-in only: Web (`ExternalAuth.cs`, OAuth ticket) → Application (`ProcessExternalLoginCallbackCommand`) → Infrastructure (new background job) via existing `Application`-owned interfaces (`IUserProfileRepository`, `IFileStorage`, `IRemoteFileDownloader`, `IBackgroundJobClient`). No new outward-pointing reference.
- **II. SOLID (SRP)** — PASS. `IIdentityService.ResolveExternalLoginAsync` keeps its single responsibility (resolve/create/link the account) unchanged; profile-field syncing is added as a distinct orchestration step in `ProcessExternalLoginCallbackCommandHandler`, which is already the CQRS orchestration point for "a login attempt just happened" and is permitted to coordinate multiple repositories (§3).
- **III. Simplicity — DRY/KISS/YAGNI** — PASS. Reuses `IUserProfileRepository.UpdateAsync`/`SetAvatarFileNameAsync`, `IFileStorage.SaveAsync`, and the existing generic `IRemoteFileDownloader` (already used by `RemoteFileDownloader` for SiteAnalysis imagery) instead of inventing a new HTTP-fetch abstraction. Only one new interface is introduced (`IExternalProfilePictureSyncJob`), directly mirroring the already-established `IPasswordEmailJob` background-job pattern — not a novel abstraction shape.
- **V. Dependency Inversion & Testability** — PASS. New `IExternalProfilePictureSyncJob` lives in `Application.Abstractions`, implemented in `Infrastructure`; `ProcessExternalLoginCallbackCommandHandler` depends on `IBackgroundJobClient` + `IUserProfileRepository` (both already mockable interfaces), so it stays unit-testable with no real DB/HTTP.
- **VI. Separation of Concerns** — PASS. OAuth/`ClaimTypes` extraction stays confined to `ExternalAuth.cs` (Web layer, the existing boundary) — the Application command receives plain `string?` values, never a `ClaimsPrincipal`.
- **VIII. No Silent Failures** — PASS by design: a picture-fetch/store failure in the background job is logged via structured Serilog (Warning) with `userId`/`provider` context (FR-008) and left for Hangfire's standard automatic retry; it is deliberately not surfaced to the user because, per the spec's explicit edge case, the user's session is already active by the time it could occur — this is the documented, spec-approved exception to "user-visible outcome," not an omission.
- **§8 Security** — Addressed in research.md: outbound fetch is host-allow-listed (Decision 6) to prevent SSRF, and downloaded content passes magic-byte validation via the new shared `IImageContentValidator` (Decision 9) before persisting — closing the same file-validation gap in the existing manual-upload path (`UploadAvatarCommandHandler`).
- **§5 Database** — PASS. No new entity, column, or migration.
- **§6 API** — PASS. No new/changed public endpoint or contract; `GET /api/v1/users/me` is unchanged.

No violations requiring justification — Complexity Tracking is not needed.

**Post-Phase-1 re-check**: PASS, unchanged. The Phase 1 design (data-model.md, contracts/,
quickstart.md) introduced exactly the two new members anticipated above
(`IExternalProfilePictureSyncJob` interface + its `Infrastructure` implementation) and no
additional interfaces, no new Application→Infrastructure references, and no schema change —
confirming the gate analysis above still holds after design.

**Post-`/speckit-analyze` re-check**: PASS, unchanged. `/speckit-analyze` identified a real
gap (CRITICAL finding C1): the design as written left the new avatar-writing path without
content-based file validation, required by constitution §8. The remediation adds one more
`Application.Abstractions` interface (`IImageContentValidator`, research.md Decision 9) plus
its `Infrastructure` implementation — the same shape as the interface already anticipated
above, not a new category of dependency — and additionally closes the identical, pre-existing
gap in `UploadAvatarCommandHandler`. No new Application→Infrastructure reference, no schema
change; the gate analysis still holds.

## Project Structure

### Documentation (this feature)

```text
specs/062-external-login-profile-sync/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md        # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # Phase 1 output (/speckit-plan command)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/
├── AskLucy.Web/
│   ├── Program.cs                                  # Google Scope("profile") + Google/Facebook ClaimActions + Fields additions
│   └── Auth/ExternalAuth.cs                        # extract GivenName/Surname/picture claims
├── AskLucy.Application/
│   ├── Abstractions/IExternalProfilePictureSyncJob.cs   # new background-job contract
│   ├── Abstractions/IImageContentValidator.cs           # new: magic-byte validation contract
│   ├── Users/Commands/UploadAvatar/UploadAvatarCommandHandler.cs  # retrofit: call IImageContentValidator
│   └── Authentication/Commands/ExternalLogin/
│       ├── ProcessExternalLoginCallbackCommand.cs        # + FirstName, LastName, PictureUrl
│       └── ProcessExternalLoginCallbackCommandHandler.cs # + name sync (sync) + job enqueue (async)
├── AskLucy.Infrastructure/
│   ├── Identity/ExternalProfilePictureSyncJob.cs   # new: IRemoteFileDownloader + IImageContentValidator + IFileStorage + IUserProfileRepository
│   ├── Files/ImageContentValidator.cs              # new: magic-byte signature checks
│   └── DependencyInjection.cs                      # register named HttpClient + job binding + validator binding
└── AskLucy.Persistence/                            # unchanged — reuses existing ApplicationUser columns

tests/
├── AskLucy.Application.Tests/Authentication/ProcessExternalLoginCallbackCommandHandlerTests.cs  # extend
├── AskLucy.Infrastructure.Tests/Identity/ExternalProfilePictureSyncJobTests.cs                    # new
└── AskLucy.Web.Tests/Auth/ExternalLoginTests.cs    # extend for new claim mapping
```

**Structure Decision**: Single existing ASP.NET Core Clean Architecture solution (`Domain`/`Application`/`Infrastructure`/`Web`/`Persistence`). This is a backend-only, additive change within the existing `Authentication`/`ExternalLogin` and `Users` feature areas — no new project, no frontend change.

## Complexity Tracking

*No Constitution Check violations — this section is not applicable.*
