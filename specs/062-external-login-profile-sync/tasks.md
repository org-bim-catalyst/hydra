---

description: "Task list for External Login Profile Sync"
---

# Tasks: External Login Profile Sync

**Input**: Design documents from `/specs/062-external-login-profile-sync/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/external-login-claim-contract.md, quickstart.md

**Tests**: Included — the constitution mandates unit/integration testing, and quickstart.md's "Automated coverage" section already commits to the three test files extended/created below.

**Organization**: Tasks are grouped by user story. Note: because all three user stories exercise the *same* claim-mapping/sync/persistence pipeline (spec.md explicitly calls this out — US2 "depends on the same underlying claim-mapping and persistence logic," US3 "shares the same implementation" as US2), the production code lives entirely in the Foundational phase. Each user story phase then adds the test coverage that independently proves that story's specific acceptance scenarios against the shared implementation.

**2026-09-21 update**: `/speckit-analyze` found a CRITICAL gap (constitution §8 — no content/magic-byte file validation on the new avatar-writing path) and a HIGH gap (FR-001's "request necessary permissions" had no task adding the Google OAuth `profile` scope, only claim *mapping*). Both are now folded into the Foundational phase (T002, T010, T012) and covered by tests in User Story 1 (T021, T023) — see research.md Decisions 1 and 9.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1, US2, US3)

## Path Conventions

Existing single-solution Clean Architecture repo: `src/AskLucy.{Web,Application,Infrastructure,Persistence}/`, `tests/AskLucy.{Application,Infrastructure,Web}.Tests/`.

---

## Phase 1: Setup

**Purpose**: Confirm the local environment can exercise the feature manually; no new project/package setup is required (plan.md's Technical Context adds no new dependency).

- [ ] T001 Verify local `Authentication:Google:ClientId`/`ClientSecret` and `Authentication:Facebook:AppId`/`AppSecret` are configured via user-secrets for `src/AskLucy.Web/` (per quickstart.md Prerequisites), so Scenarios 1–5 can be run manually after implementation.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement the full claim-mapping → command → handler → background-job pipeline, plus the shared image-validation fix identified in analysis. This is shared by all three user stories — none of the stories' acceptance scenarios can be demonstrated until this phase is complete.

**⚠️ CRITICAL**: No user story test work can begin until this phase is complete.

- [x] T002 [P] Add `options.Scope.Add("profile")` (FR-001 — required so the userinfo response actually contains `given_name`/`family_name`/`picture`; the handler's default scope does not guarantee this) and the Google picture claim mapping (`options.ClaimActions.MapJsonKey("urn:google:picture", "picture")`) to the Google handler registration in `src/AskLucy.Web/Program.cs` — done during `/speckit-analyze` remediation
- [x] T003 [P] Add Facebook name + picture field/claim mapping (`options.Fields.Add("first_name"/"last_name"/"picture")`, `MapJsonKey(ClaimTypes.GivenName, "first_name")`, `MapJsonKey(ClaimTypes.Surname, "last_name")`, `MapCustomJson("urn:facebook:picture", ...)` for the nested `picture.data.url` path) to the Facebook handler registration in `src/AskLucy.Web/Program.cs`
- [x] T004 Extract `ClaimTypes.GivenName`, `ClaimTypes.Surname`, and the provider-specific picture claim (`urn:google:picture` / `urn:facebook:picture`) in `ExternalAuth.HandleTicketReceivedAsync` in `src/AskLucy.Web/Auth/ExternalAuth.cs`, passing `null` (not empty string) when a claim is absent
- [x] T005 Extend `ProcessExternalLoginCallbackCommand` with `string? FirstName`, `string? LastName`, `string? PictureUrl` per contracts/external-login-claim-contract.md in `src/AskLucy.Application/Authentication/Commands/ExternalLogin/ProcessExternalLoginCallbackCommand.cs`
- [x] T006 Update the `ProcessExternalLoginCallbackCommand` construction call site to pass the newly extracted claims in `src/AskLucy.Web/Auth/ExternalAuth.cs` (depends on T004, T005)
- [x] T007 [P] Add `IExternalProfilePictureSyncJob` interface (`Task SyncAsync(string userId, string pictureUrl, CancellationToken cancellationToken = default)`) with the XML-doc contract from data-model.md in `src/AskLucy.Application/Abstractions/IExternalProfilePictureSyncJob.cs`
- [x] T008 In `ProcessExternalLoginCallbackCommandHandler`, after `identityService.ResolveExternalLoginAsync`, add synchronous name sync (`profiles.GetByIdAsync` → `effectiveFirstName = command.FirstName ?? current.FirstName`, same for last name → `profiles.UpdateAsync`) and enqueue `IBackgroundJobClient.Enqueue<IExternalProfilePictureSyncJob>(j => j.SyncAsync(userId, pictureUrl, CancellationToken.None))` only when `PictureUrl` is non-null, in `src/AskLucy.Application/Authentication/Commands/ExternalLogin/ProcessExternalLoginCallbackCommandHandler.cs` (depends on T005, T007)
- [x] T009 [P] Register a named `HttpClient("ExternalProfilePictureDownload")` (own timeout, matching the `"SiteAnalysisImageDownload"`/`"Geocoding"` precedent) in `src/AskLucy.Infrastructure/DependencyInjection.cs`
- [x] T010 [P] Add `IImageContentValidator` interface (`bool IsValidImage(Stream content, out string? detectedContentType)`) and a magic-byte-signature implementation (JPEG/PNG/GIF/WebP) in `src/AskLucy.Application/Abstractions/IImageContentValidator.cs` and `src/AskLucy.Infrastructure/Files/ImageContentValidator.cs` (research.md Decision 9 — constitution §8 file-validation gap closure)
- [x] T011 Implement `ExternalProfilePictureSyncJob : IExternalProfilePictureSyncJob` — host allow-list check (`*.googleusercontent.com`; `platform-lookaside.fbsbx.com`, `*.fbcdn.net`), `IRemoteFileDownloader.DownloadAsync` with a 5MB cap (`Content-Length` check + streamed byte-count abort), `IImageContentValidator.IsValidImage` before persisting, `IFileStorage.SaveAsync`, `IUserProfileRepository.SetAvatarFileNameAsync`, and a structured Serilog warning (with `userId`/provider context) on any failure without rethrowing, in `src/AskLucy.Infrastructure/Identity/ExternalProfilePictureSyncJob.cs` (depends on T007, T009, T010)
- [x] T012 [P] Retrofit `UploadAvatarCommandHandler` to call `IImageContentValidator.IsValidImage` before `IFileStorage.SaveAsync`, returning a validation-failure result on rejection, in `src/AskLucy.Application/Users/Commands/UploadAvatar/UploadAvatarCommandHandler.cs` (depends on T010; closes the same constitution §8 gap for the existing manual-upload path)
- [x] T013 Register the `IExternalProfilePictureSyncJob` → `ExternalProfilePictureSyncJob` and `IImageContentValidator` → `ImageContentValidator` DI bindings in `src/AskLucy.Infrastructure/DependencyInjection.cs` (depends on T011, T010)
- [x] T014 Update the existing positional `ProcessExternalLoginCallbackCommand` construction calls in `tests/AskLucy.Application.Tests/Authentication/ProcessExternalLoginCallbackCommandHandlerTests.cs` to compile against the new 8-argument record shape (depends on T005)

**Checkpoint**: Claim mapping (including the Google `profile` scope), extraction, the extended command, handler orchestration (sync name / async picture), image content validation, and the background job are fully wired end-to-end. User story test phases can now begin.

---

## Phase 3: User Story 1 - New user signs up via Google or Facebook (Priority: P1) 🎯 MVP

**Goal**: A brand-new account created via Google/Facebook sign-in has first name, last name, and picture populated without manual entry, and a missing/failing claim or fetch never blocks account creation.

**Independent Test**: Sign in with a Google (or Facebook) account that has a name and photo and no prior Ask Lucy account; verify the resulting profile shows all three fields (quickstart.md Scenario 1).

### Tests for User Story 1

- [x] T015 [P] [US1] Handler test: new-account sign-in with `FirstName`/`LastName`/`PictureUrl` all present sets the profile fields synchronously and enqueues `IExternalProfilePictureSyncJob` exactly once, in `tests/AskLucy.Application.Tests/Authentication/ProcessExternalLoginCallbackCommandHandlerTests.cs`
- [x] T016 [P] [US1] Handler test: new-account sign-in with `PictureUrl` absent still creates the account successfully and does not enqueue the job (FR-002, edge case), in same file
- [x] T017 [P] [US1] Handler test: a link action (`LinkToUserId` set, no prior linked login) triggers the same synchronous name sync + async picture enqueue as a full sign-in (FR-003a), in same file
- [x] T018 [P] [US1] Job test: `ExternalProfilePictureSyncJob.SyncAsync` successfully downloads an allow-listed-host image and calls `SetAvatarFileNameAsync`, in `tests/AskLucy.Infrastructure.Tests/Identity/ExternalProfilePictureSyncJobTests.cs`
- [x] T019 [P] [US1] Job test: a picture URL on a non-allow-listed host is rejected before any HTTP call is made (SSRF guard, research.md Decision 6), in same file
- [x] T020 [P] [US1] Job test: a download exceeding the 5MB cap (via `Content-Length` or streamed byte count) is aborted and no file is stored, in same file
- [x] T021 [P] [US1] Job test: a downloaded payload that fails `IImageContentValidator` (non-image or corrupted signature) is treated as a fetch failure — no file stored, structured warning logged (constitution §8), in same file
- [x] T022 [P] [US1] Job test: a fetch/storage failure logs a structured warning with `userId`/provider context, does not throw, and leaves `AvatarFileName` unchanged (FR-008), in same file
- [x] T023 [P] [US1] Web test: Google and Facebook handler registrations in `Program.cs` map `GivenName`/`Surname`/picture claims exactly as specified in contracts/external-login-claim-contract.md, and the Google handler's configured `Scope` includes `"profile"` (FR-001), in `tests/AskLucy.Web.Tests/Auth/ExternalLoginTests.cs`

**Checkpoint**: User Story 1 is independently testable — new social sign-ups get a populated profile, and no claim/fetch/validation failure blocks account creation.

---

## Phase 4: User Story 2 - Existing user's profile self-heals on next social sign-in (Priority: P2)

**Goal**: An existing account created before this feature (or with blank/stale fields) gets its name and picture populated/refreshed on its next sign-in, with no backfill job.

**Independent Test**: Sign in again via a linked provider on an account with blank first/last name and picture; verify the fields populate after that sign-in (quickstart.md Scenario 2).

### Tests for User Story 2

- [x] T024 [P] [US2] Handler test: existing account with `FirstName`/`LastName`/avatar currently null, sign-in with claims present populates all three (synchronously for name, via job enqueue for picture), in `tests/AskLucy.Application.Tests/Authentication/ProcessExternalLoginCallbackCommandHandlerTests.cs`
- [x] T025 [P] [US2] Handler test: existing account with previously-set (including manually-edited) `FirstName`/`LastName`, sign-in with differing claim values overwrites them — provider always wins (FR-006, Acceptance Scenario 2 of US2), in same file
- [x] T026 [P] [US2] Handler test: existing account sign-in where a claim is absent for one field on this specific event leaves that field's previously stored value unchanged rather than clearing it (merge-on-null, FR-004), in same file

**Checkpoint**: User Story 2 is independently testable — pre-existing accounts self-heal on their next sign-in with zero manual/admin action.

---

## Phase 5: User Story 3 - User's provider-side name or picture changes over time (Priority: P3)

**Goal**: A subsequent change to the user's name/photo on the linked provider is reflected in Ask Lucy on their next sign-in.

**Independent Test**: Change the linked provider's display name/photo, sign in again, verify the Ask Lucy profile reflects the new value (quickstart.md Scenario 3).

### Tests for User Story 3

- [x] T027 [P] [US3] Handler test: two sequential sign-ins for the same user with different `FirstName`/`LastName` claim values on the second call result in the profile being updated again to match the second call's values, in `tests/AskLucy.Application.Tests/Authentication/ProcessExternalLoginCallbackCommandHandlerTests.cs`
- [x] T028 [P] [US3] Handler test: a second sign-in with a changed `PictureUrl` enqueues `IExternalProfilePictureSyncJob` again even though a picture was already stored from the first sign-in, in same file
- [x] T029 [P] [US3] Job test: `SyncAsync` always performs the fetch/store — asserting there is no comparison against a previously-stored value/URL before fetching (FR-006's unconditional re-fetch, no change-detection), in `tests/AskLucy.Infrastructure.Tests/Identity/ExternalProfilePictureSyncJobTests.cs`

**Checkpoint**: All three user stories are independently functional and tested.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [x] T030 [P] Run `dotnet format` across the solution and fix any formatting diffs in the touched files
- [x] T031 Run the full solution test suite (`dotnet test AskLucy.sln`) and confirm all new and existing tests pass, including the T014 compile fix and the T012 `UploadAvatarCommandHandler` retrofit — Domain (305/305), Application (1484/1484), Infrastructure (400/400) all pass; Persistence.Tests intentionally skipped (gated behind `PERSISTENCE_TESTS_DEDICATED_DATABASE`); Web.Tests: initially 426/496 passed locally; the 70 failures traced to a stale local LocalDB fallback (`CustomWebApplicationFactory`'s default when `PERSISTENCE_TESTS_CONNECTION_STRING` isn't set) permanently stuck at 5 migrations from 2026-07-29, because the next one, `AddConversationFullTextSearch`, fails outright against LocalDB ("Cannot use full-text search in user instance" — a hard SQL Server LocalDB limitation, not a code bug) and `Program.cs`'s auto-migration-on-startup swallows that failure by design. Re-ran with `PERSISTENCE_TESTS_CONNECTION_STRING` set to the real shared site4now.net test database (the same one CI's secret points to, confirmed with the user) — 496/496 pass, 0 failures. Full solution: Domain 305/305, Application 1484/1484, Infrastructure 400/400, Web.Tests 496/496. All spec-062 tests (`ExternalLoginTests`, `ProcessExternalLoginCallbackCommandHandlerTests`, `ExternalProfilePictureSyncJobTests`) pass. Along the way, fixed a genuine test-harness bug in `ExternalLoginTests.ExternalLoginHandlers_ShouldMapNameAndPictureClaims_AndRequestGoogleProfileScope`: it configured Google/Facebook credentials via `ConfigureAppConfiguration`, which only takes effect at `Build()` time, too late for Program.cs's pre-`Build()` `builder.Configuration["Authentication:Google:ClientId"]` read that gates scheme registration — switched to `UseSetting`, which is visible immediately
- [ ] T032 [P] Manually execute quickstart.md Scenarios 1–5 against the local dev environment and confirm each expected outcome
- [x] T033 Re-confirm plan.md's Constitution Check (including the Post-`/speckit-analyze` re-check) still holds against the final diff — no interfaces/references introduced beyond `IExternalProfilePictureSyncJob`, `IImageContentValidator`, and their `Infrastructure` implementations — confirmed: backend diff is scoped to those two new interfaces + `AskLucy.Infrastructure/Identity/ExternalProfilePictureSyncJob.cs` + `AskLucy.Infrastructure/Files/ImageContentValidator.cs`, plus modifications to the existing external-login handler, `UploadAvatarCommandHandler`, `RemoteFileDownloader`, `ExternalAuth.cs`, DI registration, and `Program.cs`'s Google/Facebook options. No Application-layer reference to EF Core or other layering violations introduced. (Unrelated pending admin-panel frontend changes visible in `git status` predate this feature and are out of scope.)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately.
- **Foundational (Phase 2)**: Independent of Setup; BLOCKS all user story test phases. Internal order: T002/T003/T007/T009/T010 can run in parallel; T004 before T006; T005 before T006, T008, T014; T007 before T008, T011; T009 before T011; T010 before T011, T012, T013; T011 before T013.
- **User Stories (Phase 3–5)**: All depend on Foundational (Phase 2) completion. Since all three stories test the same shared implementation, their test tasks are mutually independent and can run in any order or in parallel once Phase 2 is done.
- **Polish (Phase 6)**: Depends on all desired user story phases being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Depends only on Foundational — no dependency on US2/US3.
- **User Story 2 (P2)**: Depends only on Foundational — exercises the same code as US1 with different starting account state; no dependency on US1's or US3's tasks.
- **User Story 3 (P3)**: Depends only on Foundational — exercises the same code with a second sign-in; no dependency on US1's or US2's tasks.

### Parallel Opportunities

- T002, T003, T007, T009, T010 (Foundational) can run in parallel — different concerns within shared files, no interdependency.
- T012 (Foundational) can run in parallel with T011 once T010 is done — different files.
- All tests within T015–T023 (US1), T024–T026 (US2), and T027–T029 (US3) are marked [P] — each adds a new test method/file, no shared-file write conflicts beyond appending to the same test class (sequence edits if working solo).
- T030 and T032 (Polish) can run in parallel with each other.

---

## Parallel Example: Foundational Phase

```bash
# Launch independent Foundational tasks together:
Task: "Add Google profile scope + picture claim mapping in src/AskLucy.Web/Program.cs"
Task: "Add Facebook name+picture field/claim mapping in src/AskLucy.Web/Program.cs"
Task: "Add IExternalProfilePictureSyncJob interface in src/AskLucy.Application/Abstractions/IExternalProfilePictureSyncJob.cs"
Task: "Register named HttpClient(\"ExternalProfilePictureDownload\") in src/AskLucy.Infrastructure/DependencyInjection.cs"
Task: "Add IImageContentValidator interface + magic-byte implementation"
```

## Parallel Example: User Story 1 Tests

```bash
Task: "Handler test: new-account sign-in with all claims present"
Task: "Handler test: new-account sign-in with picture claim absent"
Task: "Handler test: link action triggers same sync as full sign-in"
Task: "Job test: successful fetch+store path"
Task: "Job test: non-allow-listed host rejected"
Task: "Job test: oversized download aborted"
Task: "Job test: invalid image signature rejected"
Task: "Job test: fetch failure logs and doesn't throw"
Task: "Web test: Google/Facebook claim mapping + Scope assertion"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (implements the entire pipeline plus image validation — CRITICAL, blocks all stories)
3. Complete Phase 3: User Story 1 tests
4. **STOP and VALIDATE**: Run quickstart.md Scenario 1 manually
5. Ship — this alone satisfies SC-001

### Incremental Delivery

1. Setup + Foundational → full pipeline implemented and unit-tested via US1
2. Add User Story 2 tests → proves the self-heal behavior (SC-002) against the same code
3. Add User Story 3 tests → proves ongoing propagation (SC-003) against the same code
4. Polish → format, full suite, manual quickstart pass, constitution re-check

---

## Notes

- Because US1/US2/US3 share one implementation (per spec.md's own "Why this priority" rationale), there are no story-specific implementation tasks in Phases 4–5 — only the tests that independently prove each story's distinct acceptance scenarios.
- [P] tasks = different files or independent additions, safe to parallelize.
- Commit after each task or logical group (Foundational task, or a story's full test batch).
- Verify new tests fail against pre-T002–T013 code, then pass after, if following strict TDD.
