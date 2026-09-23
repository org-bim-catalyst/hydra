---

description: "Task list for 072 Custom Model Deployment (Admin)"
---

# Tasks: Custom Model Deployment (Admin)

**Input**: Design documents from `/specs/072-custom-model-deploy/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md) (D1–D13), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required. The user asked for tests across Domain, Application, Infrastructure and Web, including the FTP-failure and path-traversal cases, and constitution §10 applies. The required cases are listed in [plan.md → Testing strategy](plan.md#testing-strategy). In each phase, write the test tasks first and confirm they fail before implementing.

**Organization**: Tasks are grouped by user story (US1–US6, all P1). They are ordered by dependency: deploy → progress/cancel → guardrails → failures → availability → voice binding.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an incomplete task in the same phase).
- **[Story]**: the user story (US1–US6). Setup, Foundational and Polish tasks have no story label.
- Paths are repository-relative. `ClientApp/` means `src/AskLucy.Web/ClientApp/`.

## Standing rules for every task

1. **The temporary FTP design** ([plan.md](plan.md), note at the top). Only `ConfigurationDeploymentTargetSettingsProvider` may read `FtpOptions` or the `Ftp` section. Everything else goes through `IDeploymentTargetSettingsProvider`.
2. **The password**:
   - It never appears in a log, response, exception message or `ToString()`.
   - Never attach FluentFTP's own logger: it logs the `PASS` command.
   - The root path and host are never returned either.
3. **Clean Architecture**: Application never references EF Core, `HttpClient` or FluentFTP. Progress writes use `ExecuteUpdateAsync` in Persistence.
4. **No silent failures** (constitution §2 VIII):
   - Backend: every catch logs, and either rethrows or records a visible `Failed` state.
   - Frontend: every query, mutation and hub handler has a toast or inline error. No `start()` promise goes unhandled.
5. **Options**: every property has a default. Never use `ValidateOnStart()`, which crashes the whole host.
6. **DI**: no self-referential `sp => sp.GetRequiredService<T>()` factories. Background work and the locator each open their own `IServiceScopeFactory` scope.
7. **Migrations**: create with `dotnet ef migrations add AddCustomModels --project src/AskLucy.Persistence --startup-project src/AskLucy.Web`. The file has no BOM and `System` usings come first. `Down()` fully reverses `Up()`.
8. **Verification**:
   - Type-check with `npx tsc -b --noEmit`, not a bare `tsc`.
   - Run the **full** vitest suite.
   - `Web.Tests` needs `PERSISTENCE_TESTS_CONNECTION_STRING`.
   - Log assertions use `FakeLogger`. `Received().Log(...)` never matches `[LoggerMessage]`.
9. **jsdom traps**:
   - Inside open MUI dialogs, use `getByText`, not `getByRole`.
   - Add an MSW handler for every new endpoint a test renders. An unmocked endpoint is bypassed to the real network and flakes on CI.
10. **Notify only after a save** (plan.md → Complexity Tracking, the justified §3 domain-event deviation). Every `ICustomModelDeploymentNotifier` call and audit event comes **after** a successful save. When the save throws, nothing is pushed or audited.
11. **RowVersion**: progress `UPDATE`s bump the row's `rowversion`. Every aggregate save that can race them (cancel, the recovery sweep, the job's transitions) reloads and retries **once** on a concurrency conflict. A second conflict is surfaced, never swallowed (research D6).

---

## Phase 1: Setup

**Purpose**: The package, the options and the configuration files.

- [X] T001 Add `<PackageReference Include="FluentFTP" Version="55.0.0" />` to `src/AskLucy.Infrastructure/AskLucy.Infrastructure.csproj`. Run `dotnet restore` and confirm there's no version conflict.
- [X] T002 [P] Create `src/AskLucy.Application/Options/CustomModelsOptions.cs` with a `SectionName = "CustomModels"` constant and these defaults ([data-model.md](data-model.md#custommodelsoptions--section-custommodels-application--optionscustommodelsoptionscs)):
  - `MaxDeploymentBytes = 21474836480`
  - `AllowedDestinationPrefixes = ["Models","App_Data/Models"]`
  - `TempDirectory = "App_Data/Temp/custom-models"`
  - `ProgressPersistIntervalSeconds = 2`
  - `ProgressPushIntervalMilliseconds = 500`
  - `StallTimeoutSeconds = 60`
- [X] T003 [P] Create `src/AskLucy.Infrastructure/CustomModels/Deployment/FtpOptions.cs`:
  - A `sealed record` with `SectionName = "Ftp"`.
  - `Host = ""`, `Port = 21`, `Username = ""`, `Password = ""`, `RootPath = ""`, `AllowPlainFtp = false`.
  - `PrintMembers` overridden to write `Password = ***`.
  - An XML doc comment: "TEMPORARY until spec 071 Connectors — see specs/072 plan.md".
- [X] T004 [P] Add a `"CustomModels"` section with the T002 defaults to `src/AskLucy.Web/appsettings.json`. Don't put any `Ftp` values in this file.
- [X] T005 [P] Add an `"Ftp"` section to `src/AskLucy.Web/appsettings.Development.json.example`, following the file's existing placeholder style (`"<SECRET — …>"`):
  - `Host: "<ftp host>"`, `Port: 21`, `Username: "<ftp user>"`
  - `Password: "<SECRET — prefer: dotnet user-secrets set \"Ftp:Password\" ...>"`
  - `RootPath: "/hydra"`, `AllowPlainFtp: false`
- [X] T006 [P] Create `src/AskLucy.Web/appsettings.Production.json.example` (**new file**).
  - Mirror the keys of the untracked `appsettings.Production.json`, with placeholder values only.
  - Include the `Ftp` section from T005.
  - Check afterwards: `git grep -nE '"(Password|ApiKey|ConnectionString)"' -- '*.example'` shows placeholders only.
  - Confirm `appsettings.Production.json` itself is still gitignored.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Permissions, the Domain model, persistence, and the Application seams that every story uses.

**⚠️ No user-story work starts until this phase is complete.**

### Tests for Foundational

- [X] T007 [P] Extend `tests/AskLucy.Domain.Tests/Authorization/AdminPermissionCatalogTests.cs`:
  - `admin.custom-models.view` and `admin.custom-models.manage` exist under `AdminArea.CustomModels` at the View and Manage levels.
  - Neither is implied by any `admin.ai-providers.*` key.
- [X] T008 [P] Create `tests/AskLucy.Domain.Tests/CustomModels/HuggingFaceModelSourceTests.cs`. It's a theory table (research D4):
  - **Accept**:
    - `https://huggingface.co/Supertone/supertonic-3`
    - with `www.`, and over `http`
    - `/tree/main`, `/tree/v1.0`, `/tree/refs/pr/3`
    - `/resolve/main/onnx/x.onnx`, which sets `IgnoredFilePath`
    - `/blob/<sha>/README.md`
  - For accepted URLs, assert `RepositoryId`, `Revision` (default `main`) and `DerivedName`.
  - **Reject**: another host; `huggingface.co.evil.example`; `huggingface.co@evil.example`; userinfo; an IP literal; port 8443; the `ftp:` scheme; `/datasets/x/y`; `/spaces/x/y`; `/Supertone` (no repo); `..` or `.` as the owner or repo; spaces; an empty string; a relative URL.
- [X] T009 [P] Create `tests/AskLucy.Domain.Tests/CustomModels/DeploymentDestinationTests.cs`. It's a theory table (research D5), with prefixes `["Models","App_Data/Models"]`:
  - **Accept**: `Models/supertonic-3`; `App_Data/Models/whisper/large`; `Models//x/`, which canonicalises to `Models/x`.
  - **Reject**:
    - Empty input, the bare `Models`, and `wwwroot/x`
    - `../x`, `Models/../x`, `Models/%2e%2e/x`, `Models/%252e%252e/x` (double encoding)
    - `/Models/x`, `Models\x`, `C:/x`
    - `Models/x/CON`, `Models/x/nul.txt`
    - `Models/x.`, `Models/x ` (trailing space)
    - `Models/x․y` (U+2024), `Models/a:b`, and a control character
  - **`TryCombine`**: accepts `onnx/model.onnx`. Rejects `../x`, `a/../../b`, `web.config`, `sub/Web.Config`, `APP_OFFLINE.HTM` and `a\b`.
- [X] T010 [P] Create `tests/AskLucy.Domain.Tests/CustomModels/CustomModelTests.cs` (data-model state machine):
  - Every legal transition from the table.
  - Every illegal one throws `DomainRuleViolationException`.
  - `Create` starts as `Queued` + `Unavailable` + `IsInProgress`.
  - `BeginTransfer` over the cap throws with the `SizeLimitExceeded` kind.
  - `Complete` requires every file done.
  - `MakeAvailable` is allowed only when `Completed`.
  - `Remove` is allowed only for `Failed` or `Cancelled`.
  - `RequestCancellation` on a `Queued` model goes straight to `Cancelled`.
  - `RecordOverwrite` is allowed only while `Transferring`, and increments the count.
  - `IsInProgress` is false in every terminal state.
- [X] T011 [P] Create `tests/AskLucy.Persistence.Tests/CustomModels/CustomModelRepositoryTests.cs` against real SQL Server. Gate it like the other persistence tests: skipped unless `PERSISTENCE_TESTS_DEDICATED_DATABASE=1`, and never pointed at the dev database. It checks:
  - `UpdateProgressAsync` only moves forward: a lower `TransferredBytes` is ignored.
  - `UX_CustomModels_Name` refuses `Supertonic-3` when `supertonic-3` exists, and allows it again after a soft delete.
  - `UX_CustomModels_Repository_Available` refuses a second Available `supertone/SUPERTONIC-3`.
  - `UX_CustomModels_Destination_InProgress` refuses `models/X` while `Models/x` is in progress, and allows it once that job is terminal.
  - Each unique violation surfaces as `DuplicateResourceException` naming the field.
  - `AddOverwrittenFileAsync` inserts a row and increments `OverwrittenFileCount`.
  - `GetRunSignalAsync` returns `Continue`, `CancellationRequested` or `NoLongerInProgress`.
  - `FindCompletedForRepositoryAsync` ignores Queued, running, Failed, Cancelled and deleted rows, and ignores case.
  - `FindActiveJobOverlappingDestinationAsync` matches equal, parent and child paths on segment boundaries, ignoring case, and does not match `Models/ab` against `Models/a`.

### Implementation for Foundational

- [X] T012 Add `AdminArea.CustomModels` and the two permissions to `src/AskLucy.Domain/Authorization/AdminPermissionCatalog.cs`, following the `admin.ai-providers.*` entries: display names "View custom models" and "Manage custom models", with descriptions. `PermissionCatalogReconciler` picks them up at startup, so no seed migration is needed.
- [X] T013 [P] Mirror the permissions in `ClientApp/src/features/admin/adminPermissions.ts`, and update any catalogue-mirror test in that folder.
- [X] T014 [P] Create the enums `CustomModelDeploymentState.cs`, `CustomModelAvailability.cs` and `CustomModelFailureKind.cs` in `src/AskLucy.Domain/CustomModels/`. The value lists are in [data-model.md](data-model.md).
- [X] T015 [P] Implement `src/AskLucy.Domain/CustomModels/HuggingFaceModelSource.cs` to pass T008. Use pure string and `Uri` work only, with no I/O.
- [X] T016 [P] Implement `src/AskLucy.Domain/CustomModels/DeploymentDestination.cs` (with `TryCreate` and `TryCombine`) to pass T009. The root path is not a member.
- [X] T017 Implement `src/AskLucy.Domain/CustomModels/CustomModel.cs` and `CustomModelOverwrittenFile.cs` to pass T010. `CustomModel` inherits `BaseEntity`. The methods follow the [data-model state table](data-model.md#state-machine-domain-methods-an-illegal-transition-throws-domainruleviolationexception), and the cap is passed into `BeginTransfer`.
- [X] T018 Create `src/AskLucy.Persistence/Configurations/CustomModelConfiguration.cs`, following the existing configurations:
  - Tables `CustomModels` and `CustomModelOverwrittenFiles`.
  - Enums stored with `HasConversion<string>()` and the max lengths from data-model.
  - `Name`, `RepositoryId` and `Destination` use collation `SQL_Latin1_General_CP1_CI_AS`, so every uniqueness check and lookup ignores case.
  - The soft-delete query filter.
  - The three filtered unique indexes, plus `IX_CustomModels_CreatedAtUtc`, `IX_CustomModels_RepositoryId_DeploymentState` (for the locator) and the child FK index.
  - Cascade delete on the child.
- [X] T019 Add `DbSet<CustomModel>` to `src/AskLucy.Persistence/AskLucyDbContext.cs`. Generate the `AddCustomModels` migration in `src/AskLucy.Persistence/Migrations/` (standing rule 7). Review the generated SQL for the index filters and the collation.
- [X] T020 Create `src/AskLucy.Application/CustomModels/Abstractions/ICustomModelRepository.cs` with these methods:
  - `AddAsync`, `GetByIdAsync`, `ListAsync(page, pageSize)`
  - `GetOverwrittenFilesAsync(id, page, pageSize)`
  - `NameExistsAsync(name)`
  - `FindActiveJobOverlappingDestinationAsync(dest)`, which returns the in-progress model whose destination equals, contains or is contained by `dest` on a segment boundary, ignoring case (FR-015, research D5)
  - `FindAvailableForRepositoryAsync(repoId)`, `FindCompletedForRepositoryAsync(repoId)` (Completed, non-deleted only; FR-039)
  - `UpdateProgressAsync(id, CustomModelProgress)`, which only moves forward
  - `AddOverwrittenFileAsync(id, path, previousSize, utcNow)`
  - `GetRunSignalAsync(id)`, returning `Continue`, `CancellationRequested` or `NoLongerInProgress` (research D7)
  - `ListInProgressAsync()`, which includes `Queued` rows and their `BackgroundJobId`

  Then implement it in `src/AskLucy.Persistence/Repositories/CustomModelRepository.cs`:
  - Progress and overwrite writes use `ExecuteUpdateAsync` / `ExecuteSqlAsync`.
  - A unique-index `DbUpdateException` maps to `DuplicateResourceException`, with a message naming the field, following the existing repositories.
  - Register it in `src/AskLucy.Persistence/DependencyInjection.cs`.
- [X] T021 [P] Create `src/AskLucy.Application/CustomModels/Abstractions/IDeploymentTargetSettingsProvider.cs` and `DeploymentTargetSettings.cs`:
  - `DeploymentTargetSettings` is a sealed record whose `PrintMembers` redacts `Password`.
  - The XML doc comment marks it as **the Connectors swap point**.
- [X] T022 [P] Create the other Application seams in `src/AskLucy.Application/CustomModels/Abstractions/`. Signatures are in [plan.md → Project Structure](plan.md#source-code):
  - `IDeploymentFileUploader.cs` and `IDeploymentUploadSession.cs`, with `GetRemoteFileSizeAsync` returning `long?`, `UploadAsync(localPath, remoteRelativePath, IProgress<long>, ct)` and `DisposeAsync`. The session takes paths relative to the root; only the adapter joins them to the root.
  - `DeploymentTargetException.cs` (a `Kind` plus a safe message).
  - `IModelRepositorySource.cs` (`ResolveRevisionAsync` → `ResolvedRevision(commitSha, isPrivate, isGated)`, `ListFilesAsync` → `ModelRepositoryFile(path, size, sha256?, gitBlobSha1)`, `DownloadAsync(repoId, sha, path, destinationStream, IProgress<long>, ct)`).
  - `ModelRepositorySourceException.cs` (a `Kind` plus a safe message).
  - `ICustomModelDeploymentNotifier.cs`, `ICustomModelDeploymentCancellationRegistry.cs` and `ICustomModelDeploymentJob.cs`.
- [X] T023 [P] Create `src/AskLucy.Application/CustomModels/CustomModelDtos.cs`:
  - `CustomModelSummaryDto`, `CustomModelDetailDto`, `OverwrittenFileDto`, `DeploymentStatusDto`, `SourcePreviewDto`, `CustomModelProgressDto`, exactly as in [contracts/admin-custom-models.md](contracts/admin-custom-models.md#shapes).
  - A mapping from `CustomModel` that computes `canMakeAvailable`, `availabilityBlockedReason`, `canRemove` and `canCancel`.
- [X] T024 [P] Create `src/AskLucy.Application/CustomModels/CustomModelAdminActionLog.cs`, a `static partial` class of `[LoggerMessage]` security events following `src/AskLucy.Application/Ai/AiAdminActionLog.cs`:
  - Events: Submitted, CancelRequested, Completed, Failed, FileOverwritten, AvailabilityChanged, Removed, and PlainFtpInUse (a warning).
  - Each event carries only the actor id, model id, name, relative paths, kind and reason. Never the password, host or root.
- [X] T025 Implement the **temporary** `src/AskLucy.Infrastructure/CustomModels/Deployment/ConfigurationDeploymentTargetSettingsProvider.cs`:
  - It reads `IOptionsMonitor<FtpOptions>` and returns null when `Host`, `Username` or `RootPath` is blank, or when `RootPath` doesn't start with `/`.
  - It maps everything else to `DeploymentTargetSettings`.
  - A class comment points to plan.md's temporary-design note.

  Register it in `src/AskLucy.Infrastructure/DependencyInjection.cs` with `services.Configure<FtpOptions>(config.GetSection("Ftp"))` and `Configure<CustomModelsOptions>`, **with no `ValidateOnStart`**.
- [X] T026 [P] Create `tests/AskLucy.Infrastructure.Tests/CustomModels/ConfigurationDeploymentTargetSettingsProviderTests.cs`:
  - Blank or invalid fields → null. Valid fields → mapped.
  - `FtpOptions.ToString()` and `DeploymentTargetSettings.ToString()` never contain the password value.
  - Changing the options monitor is picked up without rebuilding.
- [X] T027 [P] Create `ClientApp/src/features/admin/api/adminCustomModelsApi.ts`, with typed shapes from the contract and functions for every endpoint. Follow the style of `adminAiProvidersApi.ts`, with query keys under `['admin','custom-models']`.

**Checkpoint**: The solution builds, the Domain and Foundational tests pass, and the migration applies.

---

## Phase 3: User Story 1 - Deploy a model from a Hugging Face repository (Priority: P1) 🎯 MVP

**Goal**: Add model → the server pins the commit, lists the repository, and streams each file through temp storage to `{RootPath}/{destination}/{path}` over FTPS. The row ends `Completed`.

**Independent Test**: [quickstart §3](quickstart.md#3-end-to-end-supertonic-sc-008), steps 1–3 and 5 (list refresh is fine; live updates come with US2).

### Tests for User Story 1

- [X] T028 [P] [US1] Create `tests/AskLucy.Application.Tests/CustomModels/SubmitCustomModelDeploymentCommandHandlerTests.cs`:
  - Valid → record `Queued`, job enqueued via a fake `IBackgroundJobClient`, `BackgroundJobId` stored, Submitted audit (`FakeLogger`), notifier called.
  - Settings null → a "not configured" error, nothing saved.
  - Derived name taken and no name given → 409 naming the model.
  - An explicit name succeeds.
  - Destination with an active job → 409. So does an overlapping one (`Models/a` against an active `Models/A/b`, and the reverse), naming the model. `Models/ab` against an active `Models/a` is accepted.
  - `IgnoredFilePath` is present in the response.
  - When the save throws, the notifier isn't called, no Submitted audit is written, and nothing is enqueued (standing rule 10).
- [X] T029 [P] [US1] Create `tests/AskLucy.Application.Tests/CustomModels/SubmitCustomModelDeploymentCommandValidatorTests.cs`: required fields, maximum lengths, source and destination errors keyed by `source` and `destination`, and the name regex.
- [X] T030 [P] [US1] Create `tests/AskLucy.Application.Tests/CustomModels/CustomModelDeploymentJobTests.cs` (the happy path), with fakes for the source, uploader, repository, notifier and settings:
  - The states go `Queued → Listing → Transferring → Completed`.
  - Every file is downloaded from the **resolved commit sha**, not the branch.
  - A source typed as `supertone/Supertonic-3` is stored with the canonical `RepositoryId` from the revision response (research D3 step 4).
  - Each file is uploaded to `destination/relativePath`.
  - The remote size is verified.
  - Temp files are deleted after each file, and the temp folder is gone at the end.
  - A private or gated repository → Failed(`SourceGatedOrPrivate`).
- [X] T031 [P] [US1] Create `tests/AskLucy.Infrastructure.Tests/CustomModels/HuggingFaceModelRepositorySourceTests.cs`, using a stubbed `HttpMessageHandler` (the happy path; the guard tests are in US3):
  - The revision endpoint parses the `id`, `sha`, `private` and `gated` fields. Capture the fixture from the live API, and confirm `id` is present.
  - The tree endpoint follows the `Link` next cursor.
  - A download follows the 302 to `us.aws.cdn.hf.co` and streams the bytes.
  - The SHA-256 of an LFS file and the git-blob SHA-1 of a small file are verified.
  - The constructed URLs escape path segments.
- [X] T032 [P] [US1] Create `tests/AskLucy.Infrastructure.Tests/CustomModels/FluentFtpDeploymentFileUploaderTests.cs`, with FluentFTP behind a thin internal `IFtpClientFacade` so it can be faked:
  - Explicit TLS by default, and `EncryptionMode.None` only when `AllowPlainFtp` is set. **Never `Auto`.**
  - `ValidateAnyCertificate = false`.
  - The remote path is `{RootPath}/{relative}` with exactly one slash between them.
  - Uploads use `FtpRemoteExists.Overwrite` with `createRemoteDir: true`.
  - `GetRemoteFileSizeAsync` returns null for `-1`.
- [X] T033 [P] [US1] Create `tests/AskLucy.Web.Tests/CustomModels/AdminCustomModelsControllerTests.cs` (the happy path):
  - `POST` returns 202 with a `Location` header.
  - `GET` list and `GET deployment-status` return 200.
  - The status body contains **no** host, username, password or root path.
  - `POST source-preview` returns the derived name and `nameAvailable`, with no outbound call. Assert that the Hugging Face client isn't invoked.
- [X] T034 [P] [US1] Create `ClientApp/src/features/admin/components/customModels/AddCustomModelDialog.test.tsx` (use `getByText` inside the dialog):
  - Only two fields are shown at first.
  - A preview with `nameAvailable: false`, or with no derived name, reveals the required Name field.
  - Server 400 errors show inline under the right field.
  - The `ignoredFilePath` notice is shown.
  - A submit error shows a toast.
  - When the status is `isConfigured: false`, the dialog shows "deployment not configured" and submitting is disabled.
  - Also create `AddCustomModelDialog.a11y.test.tsx`, following `pages/AdminAiProvidersPage.a11y.test.tsx`: no axe violations with the dialog open, both with and without the Name field and with inline errors showing.
- [X] T035 [P] [US1] Create `ClientApp/src/features/admin/components/customModels/CustomModelsSection.test.tsx`: the empty state with Add model, rows showing name/repo@revision/destination/size/state, and a list-fetch error showing an inline error with retry. Add MSW handlers for every endpoint it calls. Also create `CustomModelsSection.a11y.test.tsx`: no axe violations in the empty state and with rows in each deployment state.

### Implementation for User Story 1

- [X] T036 [US1] Implement `src/AskLucy.Infrastructure/CustomModels/HuggingFace/HuggingFaceModelRepositorySource.cs` to pass T031:
  - A named HttpClient `"HuggingFace"`, registered in `src/AskLucy.Infrastructure/DependencyInjection.cs` with `AllowAutoRedirect = false`, a 60 s timeout and a `User-Agent`.
  - Redirects are followed by hand (the allowlist itself is added in US3, T056).
  - Downloads use `ResponseHeadersRead`.
  - Hashes are computed with an incremental hash while the file streams to the destination stream.
  - URLs are built only from the parsed `owner/repo/sha`, with each path segment escaped by `Uri.EscapeDataString`.
  - Failures are wrapped as `ModelRepositorySourceException` with a safe message.
- [X] T037 [US1] Implement `src/AskLucy.Infrastructure/CustomModels/Deployment/FluentFtpDeploymentFileUploader.cs` (plus the internal `IFtpClientFacade` / `AsyncFtpClientFacade`) to pass T032:
  - One `AsyncFtpClient` per session.
  - `ValidateCertificate` accepts only `SslPolicyErrors.None`.
  - `ReadTimeout` and `DataConnectionReadTimeout` are 60 s.
  - `UploadFile(..., FtpRemoteExists.Overwrite, createRemoteDir: true, FtpVerify.None, progress)`.
  - It reconnects once *between* files.
  - Register it as a singleton factory `IDeploymentFileUploader`.
- [X] T038 [US1] Create `src/AskLucy.Application/CustomModels/Commands/SubmitCustomModelDeployment/` (`SubmitCustomModelDeploymentCommand.cs`, `…Validator.cs`, `…Handler.cs`) to pass T028 and T029. The flow is in [plan.md → Key flows](plan.md#key-flows) step 1.
  - Enqueue with `IBackgroundJobClient.Enqueue<ICustomModelDeploymentJob>(j => j.RunAsync(id, CancellationToken.None))` **after** the save.
  - If enqueueing throws, mark the record Failed(`Unexpected`), save, log, and rethrow.
  - Refuse an equal or overlapping active destination through `FindActiveJobOverlappingDestinationAsync`, with a 409 naming the model.
- [X] T039 [US1] Create `src/AskLucy.Application/CustomModels/Jobs/CustomModelDeploymentJob.cs` (the happy path) to pass T030:
  - `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]`.
  - Each DB step opens an `IServiceScopeFactory` scope.
  - The temp folder is `{ContentRoot}/{TempDirectory}/{id}`.
  - After `ResolveRevisionAsync`, store the canonical `RepositoryId` from the response.
  - Per file: download to temp → upload → verify the remote size → delete the temp file.
  - The whole run is wrapped in `try` / `catch` / `finally` with the finally cleanup. The failure and cancel branches are completed in US2 and US4.
  - Register the job in `src/AskLucy.Application/DependencyInjection.cs`, following `PasswordResetIssuanceJob`.
- [X] T040 [P] [US1] Create `src/AskLucy.Application/CustomModels/Queries/ListCustomModels/`, `GetDeploymentStatus/` and `PreviewCustomModelSource/`:
  - `GetDeploymentStatus` returns `isConfigured`, the transport (FTPS/FTP), the cap and the prefixes. Nothing else.
  - `PreviewCustomModelSource` parses only, and checks name availability.
- [X] T041 [US1] Create `src/AskLucy.Web/Controllers/v1/AdminCustomModelsController.cs`:
  - `[ApiController][EnableRateLimiting("admin-endpoints")][Route("api/v1/admin/custom-models")]`, taking `ISender`.
  - `[RequirePermission]` per action, as in the contract table.
  - US1 endpoints: `GET`, `GET deployment-status`, `POST source-preview`, `POST` (returns 202 via `AcceptedAtAction`).
  - Map "not configured" to 400 with `type …/deployment-not-configured` in the existing `ProblemDetailsMiddleware` mapping, or via a dedicated exception type.
- [X] T042 [US1] Implement `ClientApp/src/features/admin/components/customModels/AddCustomModelDialog.tsx` to pass T034:
  - React Hook Form and Zod.
  - A debounced `source-preview` call drives the Name field.
  - The destination field shows the allowed prefixes as helper text.
- [X] T043 [US1] Implement `ClientApp/src/features/admin/components/customModels/CustomModelsSection.tsx` to pass T035: a TanStack Query list, a paper/table styled to match `ProviderModelsSection.tsx`, the Add model button (manage only), and the empty state.
- [X] T044 [US1] Update `ClientApp/src/features/admin/pages/AdminAiProvidersPage.tsx` and `ClientApp/src/features/admin/adminNav.tsx`:
  - Render `CustomModelsSection` below the providers table when the user has `admin.custom-models.view`.
  - Add `admin.custom-models.view` to the `/admin/ai-providers` nav permission alternatives.
  - Gate the providers-table query with `enabled: hasPermission('admin.ai-providers.view')`, and hide that table otherwise (research D11).
  - Update `AdminAiProvidersPage.test.tsx` and `.a11y.test.tsx` for the new section, with MSW handlers.

**Checkpoint**: A deployment of a small public repository completes end-to-end against the configured FTP server.

---

## Phase 4: User Story 2 - Watch progress live and cancel (Priority: P1)

**Goal**: Per-file and overall progress pushed at least every 2 s, persisted every 2 s, shown across tabs; cancel within 5 s; overwrite reporting (FR-010a).

**Independent Test**: [quickstart §3](quickstart.md#3-end-to-end-supertonic-sc-008) steps 3–5, plus the Cancel item in [§5](quickstart.md#5-failure-visibility-sc-003-sc-005-sc-006).

### Tests for User Story 2

- [X] T045 [P] [US2] Extend `tests/AskLucy.Application.Tests/CustomModels/CustomModelDeploymentJobTests.cs` (progress, overwrite, cancel), using a fake `TimeProvider`:
  - Progress is persisted no more often than every `ProgressPersistIntervalSeconds`, and always at file boundaries.
  - Progress is pushed no more often than every 500 ms, and always at file boundaries and state changes.
  - Overall bytes count uploaded bytes only and never decrease.
  - Existing remote file → `AddOverwrittenFileAsync(path relative to root, previous size)`, a FileOverwritten audit event, and the progress event's `overwrote` field set.
  - Registry token cancelled mid-file → `Cancelled`, temp folder deleted, final state event pushed.
  - A DB cancellation flag seen on flush (`GetRunSignalAsync` → `CancellationRequested`) → `Cancelled`.
  - `GetRunSignalAsync` → `NoLongerInProgress` (the sweep failed the record) → the job stops, leaves the record `Failed`, and never calls `Complete`.
  - Every progress event carries `deploymentState`.
  - A job delivered when the record is already `Cancelled` returns without doing anything.
- [X] T046 [P] [US2] Create `tests/AskLucy.Application.Tests/CustomModels/CancelCustomModelDeploymentCommandHandlerTests.cs`:
  - `Queued` → `Cancelled`, and the Hangfire job is deleted.
  - `Transferring` → the flag is set and the registry is signalled.
  - A terminal state → 409.
  - A CancelRequested audit event is written, only after the save succeeds.
  - A concurrency conflict on the save (a progress write bumped RowVersion) reloads and retries once. A second conflict is surfaced, not swallowed.
- [X] T047 [P] [US2] Create `tests/AskLucy.Web.Tests/CustomModels/CustomModelDeploymentHubTests.cs`:
  - A user with `admin.custom-models.view` joins `custom-model-viewers`.
  - A custom role that has the permission joins too.
  - A user with only `admin.ai-providers.*` is refused.
  - An anonymous user is refused.
- [X] T048 [P] [US2] Create `ClientApp/src/features/admin/hooks/useCustomModelDeploymentsHub.test.ts`:
  - It connects only when `isAuthenticated`.
  - A progress event patches the cached row. A state event replaces it.
  - An unknown id triggers a refetch. `onreconnected` invalidates the query.
  - `isLive` is false on close.
  - A `start()` rejection surfaces and isn't left unhandled.
- [X] T049 [P] [US2] Create `ClientApp/src/features/admin/components/customModels/CustomModelProgress.test.tsx` and `OverwrittenFilesDialog.test.tsx`:
  - The overall and per-file bars and the phase label render.
  - The "Live updates disconnected — reconnecting…" banner shows while `isLive` is false.
  - Cancel is shown only for manage users when `canCancel` is true, asks for confirmation, and shows a toast if it fails.
  - The overwritten-files list pages and shows relative paths and previous sizes.
  - Also create `OverwrittenFilesDialog.a11y.test.tsx` and `CustomModelProgress.a11y.test.tsx`: no axe violations, and the progress bars have accessible names and values.

### Implementation for User Story 2

- [X] T050 [US2] Create `src/AskLucy.Infrastructure/CustomModels/CustomModelDeploymentHub.cs`, following `src/AskLucy.Infrastructure/SiteAnalysis/SiteAnalysisHub.cs` but gated by permission:
  - `[Authorize]`. `OnConnectedAsync` resolves `IEffectivePermissionResolver` for the caller.
  - Callers with the permission join the `custom-model-viewers` group. Anyone else gets `HubException("Forbidden")`, and the attempt is logged.
  - Map it at `/hubs/custom-model-deployments` in `src/AskLucy.Web/Program.cs`, next to the existing `MapHub` calls.
- [X] T051 [US2] Create `src/AskLucy.Infrastructure/CustomModels/CustomModelDeploymentNotifier.cs`:
  - It uses `IHubContext<CustomModelDeploymentHub>` and sends `CustomModelDeploymentProgress` (including `deploymentState`) and `CustomModelDeploymentStateChanged`, as in [contracts/custom-model-deployment-hub.md](contracts/custom-model-deployment-hub.md).
  - If a send fails, it logs and doesn't throw into the job.
  - Register it as a singleton.
- [X] T052 [US2] Create `src/AskLucy.Infrastructure/CustomModels/CustomModelDeploymentCancellationRegistry.cs`: a singleton `ConcurrentDictionary<Guid, CancellationTokenSource>` with `Register`, `TryCancel` and `Unregister`, disposing each source when it is unregistered.
- [X] T053 [US2] Complete `CustomModelDeploymentJob.cs` to pass T045:
  - Link the Hangfire token and the registry token.
  - Throttle persistence and pushes with an injected `TimeProvider`.
  - Before each upload, check the remote size to record overwrites (`RecordOverwrite`, then `AddOverwrittenFileAsync`, then audit).
  - Read `GetRunSignalAsync` on each flush. Stop on `CancellationRequested` (→ `MarkCancelled`) or `NoLongerInProgress` (→ stop without any further write).
  - The `OperationCanceledException` branch ends in `MarkCancelled`, followed by cleanup and a notification.
- [X] T054 [US2] Create `src/AskLucy.Application/CustomModels/Commands/CancelCustomModelDeployment/` (command and handler) and `Queries/GetCustomModel/`, which returns the detail with paged overwritten files. Add `POST {id}/actions/cancel` (constitution §6's `{id}/actions/{verb}` convention) and `GET {id}` to `AdminCustomModelsController.cs`. The cancel handler retries once on a concurrency conflict (standing rule 11).
- [X] T055 [US2] Implement the frontend to pass T048 and T049:
  - `ClientApp/src/features/admin/hooks/useCustomModelDeploymentsHub.ts`, following `useSiteAnalysisHub.ts` (`isAuthenticated` dependency, `withAutomaticReconnect`, `isLive`).
  - `components/customModels/CustomModelProgress.tsx` and `OverwrittenFilesDialog.tsx`.
  - Wire the hook, the Cancel action and the overwritten-files link into `CustomModelsSection.tsx`.

**Checkpoint**: Progress shows live in two tabs, a reload shows current progress, Cancel works within 5 s, and overwrites are listed.

---

## Phase 5: User Story 3 - Unsafe or oversized requests are rejected (Priority: P1)

**Goal**: Reject non-Hugging Face sources, SSRF through redirects or DNS, path traversal, reserved destinations and file names, and anything over the size cap, before any byte moves.

**Independent Test**: [quickstart §4](quickstart.md#4-guardrails-sc-002--each-must-be-refused-before-any-job-starts) and the size-cap item in [§5](quickstart.md#5-failure-visibility-sc-003-sc-005-sc-006).

### Tests for User Story 3

- [X] T056 [P] [US3] Extend `tests/AskLucy.Infrastructure.Tests/CustomModels/HuggingFaceModelRepositorySourceTests.cs` with the redirect guards:
  - Refused: a redirect to `evil.example`, to `http://cdn-lfs.hf.co` (not https), and to `huggingface.co.evil.example`.
  - Refused: a 6th redirect.
  - Allowed: `cdn-lfs-us-1.hf.co` and `cas-bridge.xethub.hf.co`.
  - In a separate `SafeConnectCallbackTests.cs`: loopback, 10/8, 172.16/12, 192.168/16, 169.254/16, `::1`, `fc00::/7` and unspecified addresses are refused; a public address is allowed.
- [X] T057 [P] [US3] Extend `tests/AskLucy.Application.Tests/CustomModels/CustomModelDeploymentJobTests.cs`. In each case below the job fails during **Listing** with **zero** uploader calls:
  - Total size over the cap → Failed(`SizeLimitExceeded`), and the reason quotes the size and the limit.
  - A listing path containing `../x` → Failed(`UnsafeRepositoryPath`).
  - A `sub/web.config` → Failed(`ReservedFileName`).
- [X] T058 [P] [US3] Create `tests/AskLucy.Web.Tests/CustomModels/AdminCustomModelsGuardrailTests.cs`:
  - Every source and destination in the quickstart §4 table returns 400 Problem Details with `errors.source` or `errors.destination`.
  - No record is created and no job is enqueued.

### Implementation for User Story 3

- [X] T059 [US3] Create `src/AskLucy.Infrastructure/CustomModels/HuggingFace/HuggingFaceRedirectPolicy.cs`. It requires https and a host of `huggingface.co`, `*.huggingface.co` or `*.hf.co`, with at most 5 hops. Use it inside `HuggingFaceModelRepositorySource`'s redirect loop.
- [X] T060 [US3] Create `src/AskLucy.Infrastructure/CustomModels/HuggingFace/SafeConnectCallback.cs`: a `SocketsHttpHandler.ConnectCallback` that resolves DNS itself and refuses private addresses. Wire it into the `"HuggingFace"` client's primary handler in `DependencyInjection.cs`.
- [X] T061 [US3] In `CustomModelDeploymentJob.cs`, validate the whole listing before `BeginTransfer`: every path goes through `DeploymentDestination.TryCombine`, the cap is checked through `BeginTransfer`, and each failure maps to its `FailureKind`. Confirm T057 passes.

**Checkpoint**: None of the quickstart §4 inputs reach the job, and a repository containing an unsafe file or over the cap fails before any upload.

---

## Phase 6: User Story 4 - Failures are visible, never a stall (Priority: P1)

**Goal**: Every failure mode ends in a persisted `Failed` state with a safe reason, visible in the UI, and logged. The password never leaks.

**Independent Test**: [quickstart §5](quickstart.md#5-failure-visibility-sc-003-sc-005-sc-006) (wrong password, restart mid-transfer, hub drop) and [§2](quickstart.md#2-ftps-reachability-run-once-before-first-production-use).

### Tests for User Story 4

- [X] T062 [P] [US4] Extend `tests/AskLucy.Infrastructure.Tests/CustomModels/FluentFtpDeploymentFileUploaderTests.cs` with the **FTP failure mapping**. Each case asserts the resulting `DeploymentTargetException.Kind` and that the message and `ToString()` **don't contain the password**:
  - `FtpAuthenticationException` or code 530 → `AuthRejected`.
  - An AUTH TLS refusal → `TlsNotAccepted`.
  - `AuthenticationException` from the certificate check → `CertificateInvalid`.
  - An `IOException` or socket reset mid-upload → `ConnectionLost`, and the message names the file.
  - Code 550 → `WriteRejected`.
  - `TimeoutException` → `ConnectionLost`.
  - A remote size mismatch → `SizeMismatch`.
- [X] T063 [P] [US4] Extend `tests/AskLucy.Application.Tests/CustomModels/CustomModelDeploymentJobTests.cs` with the job's failure paths:
  - Each `DeploymentTargetException` or `ModelRepositorySourceException` kind → `Fail(kind, reason)`, then a Failed audit event, a state notification and a rethrow.
  - Temp files are cleaned in every case.
  - A `FakeLogger` scan confirms the password string appears in no log record.
  - A job delivered while the record is in `Transferring` → Failed(`InterruptedByRestart`), and the job returns without rethrowing.
  - Settings becoming null between submit and run → Failed(`TargetNotConfigured`).
  - A terminal write that finds the record no longer in progress (the sweep failed it) leaves it `Failed`; the job logs and returns without rethrowing.
  - A RowVersion conflict on a terminal write reloads, re-evaluates and retries once.
  - Not enough free disk space → Failed(`DiskSpaceExhausted`).
  - `AllowPlainFtp` → a PlainFtpInUse warning is logged at the start.
- [X] T064 [P] [US4] Extend `HuggingFaceModelRepositorySourceTests.cs` with source failures:
  - 404 → `SourceNotFound`.
  - 429 with `Retry-After: 2` retries, then succeeds.
  - Persistent 503 → `SourceUnavailable` after 3 retries.
  - No bytes for `StallTimeoutSeconds` → `DownloadStalled`, using a fake `TimeProvider` and a stalling stream.
  - A SHA-256 mismatch → `IntegrityMismatch`.
- [X] T065 [P] [US4] Create `tests/AskLucy.Infrastructure.Tests/CustomModels/CustomModelDeploymentRecoveryHostedServiceTests.cs`:
  - At startup, records in `Listing` or `Transferring` → Failed(`InterruptedByRestart`), and their temp folders are deleted.
  - `Queued` records with a live Hangfire job (Enqueued, Scheduled or Processing) are left alone.
  - `Queued` records with no `BackgroundJobId`, a missing Hangfire job, or a job in any other state → Failed(`InterruptedByRestart`).
  - A concurrency conflict while failing a row reloads and retries once.
  - Any other temp folders with no matching in-progress record are deleted.
- [X] T066 [P] [US4] Create `tests/AskLucy.Web.Tests/CustomModels/CustomModelPasswordRedactionTests.cs`. Using a factory configured with a sentinel `Ftp:Password`, call every endpoint (list, detail, status, preview, submit, and a submit that fails because the source is bad) and assert that no response body or header contains the sentinel. Then read the persisted `CustomModels` row and its `CustomModelOverwrittenFiles` rows straight from the database, and assert that no column value contains the sentinel (SC-006).
  - Also create `tests/AskLucy.Web.Tests/CustomModels/CustomModelsNotConfiguredTests.cs`, with a factory whose configuration has **no** `Ftp` section. The host boots (no `ValidateOnStart` crash), `GET deployment-status` returns `isConfigured: false`, and `POST` returns 400 with `type …/deployment-not-configured`.
- [X] T067 [P] [US4] Extend `CustomModelsSection.test.tsx`: a `Failed` row shows the failure reason prominently, with its kind as a chip; "deployment not configured" disables Add model with an explanation; and the `FTP` transport shows a warning chip.

### Implementation for User Story 4

- [X] T068 [US4] Complete the error mapping in `FluentFtpDeploymentFileUploader.cs` and the `ModelRepositorySourceException` kinds in `HuggingFaceModelRepositorySource.cs` to pass T062 and T064:
  - 429/5xx retries use backoff of 2, 8 and 30 s, honouring `Retry-After` up to 60 s.
  - Add a read-idle watchdog stream wrapper.
  - The original exception is logged as the inner exception, never interpolated into the message.
- [X] T069 [US4] Complete the job's failure branches in `CustomModelDeploymentJob.cs` to pass T063:
  - `catch (Exception)` → `Fail`, save (in a fresh scope), audit, notify, rethrow.
  - Guard delivery against a non-`Queued` state.
  - Terminal writes (`Complete`, `Fail`, `MarkCancelled`) reload the record in a fresh scope and apply only while it's still in progress, retrying once on a RowVersion conflict (research D6).
  - Check free disk space before each file, and map a disk-full `IOException` to `DiskSpaceExhausted`.
- [X] T070 [US4] Create `src/AskLucy.Infrastructure/CustomModels/CustomModelDeploymentRecoveryHostedService.cs` to pass T065, using its own scope, and register it in `DependencyInjection.cs`. Check each `Queued` row's Hangfire job state through `JobStorage.Current.GetMonitoringApi()` or `IStorageConnection.GetStateData`.
- [X] T071 [US4] Update `CustomModelsSection.tsx` (failure reason, kind chip, not-configured state, transport warning) to pass T067.

**Checkpoint**: Every row in the quickstart §5 table ends visibly as `Failed`, and the password grep returns 0.

---

## Phase 7: User Story 5 - Choose which custom models are available (Priority: P1)

**Goal**: A completed model starts Unavailable. The admin toggles Available/Unavailable (audited, only one Available per repository) and can remove Failed or Cancelled rows.

**Independent Test**: The US5 acceptance scenarios in [spec.md](spec.md), plus [quickstart §6](quickstart.md#6-permissions-fr-025-fr-026).

### Tests for User Story 5

- [X] T072 [P] [US5] Create `tests/AskLucy.Application.Tests/CustomModels/SetCustomModelAvailabilityCommandHandlerTests.cs`:
  - Completed → Available, with an audit event and a notification.
  - A model whose deployment hasn't completed → 400 "deployment has not completed".
  - Another model already Available for the same repository → 409 naming it.
  - Making a model Unavailable is always allowed.
- [X] T073 [P] [US5] Create `tests/AskLucy.Application.Tests/CustomModels/RemoveCustomModelCommandHandlerTests.cs`:
  - Failed or Cancelled → soft-deleted, the name is free again, and a Removed audit event is written.
  - Completed or in progress → 400.
  - The uploader is **never** called.
- [X] T074 [P] [US5] Extend `tests/AskLucy.Web.Tests/CustomModels/AdminCustomModelsControllerTests.cs`:
  - Every endpoint returns 403 without the permission and 2xx with it.
  - A role with `view` only gets 403 on the manage endpoints.
  - A role with only `admin.ai-providers.manage` gets 403 everywhere.
  - `PUT availability` and `DELETE` return the contract status codes.
  - Two concurrent `PUT Available` calls for the same repository end in one 200 and one 409 (the index backstop).
- [X] T075 [P] [US5] Extend `CustomModelsSection.test.tsx`:
  - The availability switch is disabled with its reason when `canMakeAvailable` is false.
  - A 409 shows a toast naming the conflicting model.
  - Remove is shown only when `canRemove` is true, asks for confirmation, and removes the row.
  - A view-only user sees no manage controls.

### Implementation for User Story 5

- [X] T076 [US5] Create `src/AskLucy.Application/CustomModels/Commands/SetCustomModelAvailability/` and `Commands/RemoveCustomModel/` (commands, validators and handlers) to pass T072 and T073. Add `PUT {id}/availability` and `DELETE {id}` to `AdminCustomModelsController.cs`.
- [X] T077 [US5] Add an availability control (following `AiModelStatusMenu.tsx`) and a Remove action with a confirmation dialog to `CustomModelsSection.tsx` to pass T075.

**Checkpoint**: The availability toggle and Remove work, are audited, and are permission-gated.

---

## Phase 8: User Story 6 - The voice engine runs from its custom model (Priority: P1)

**Goal**: Supertonic loads from its Available custom model's folder. It's hidden from "+" and shown as "model unavailable" when its model is Unavailable, and the router fails over. With no record it behaves exactly as before.

**Independent Test**: [quickstart §3](quickstart.md#3-end-to-end-supertonic-sc-008) steps 6–8.

### Tests for User Story 6

- [X] T078 [P] [US6] Create `tests/AskLucy.Infrastructure.Tests/CustomModels/ScopedHostedModelLocatorTests.cs`:
  - No record → `NoRecord`.
  - Only Queued, running, Failed, Cancelled or deleted records → `NoRecord`. A failed first deploy never takes the engine off its configured folder (FR-039).
  - A Completed, Available record → `Available(destination, id)`, even when it was typed with different casing (`supertone/SUPERTONIC-3`).
  - Completed records exist but none is Available → `Unavailable(reason)`.
  - A new scope is opened for each call.
  - No caching: a change is seen on the next call.
- [X] T079 [P] [US6] Extend `tests/AskLucy.Infrastructure.Tests/` Supertonic tests (for example `Ai/Supertonic/SupertonicModelTests.cs`):
  - `NoRecord` loads from `SupertonicOptions.ModelDirectory`.
  - `Available` loads from `{ContentRoot}/{destination}`.
  - A directory change disposes the old sessions and loads new ones.
  - `Unavailable` disposes the sessions and throws `AiProviderUnavailableException` with the "marked unavailable in Custom Models" reason.
  - Missing files under an Available folder → "model files not found on this server".
- [X] T080 [P] [US6] Extend `tests/AskLucy.Application.Tests/` voice query tests (`GetVoiceEngines`, `GetAdminVoiceProviders`):
  - Supertonic is hidden when `Unavailable`, and shown for `NoRecord` (including while a first deployment runs or after it fails) and `Available`.
  - ElevenLabs is unaffected.
  - `ModelStatus` and `ModelStatusReason` are filled.
  - Extend the `VoiceProviderRouter` tests: an `AiProviderUnavailableException` from Supertonic fails over to the next provider and records a failover event (FR-037, SC-009).
- [X] T081 [P] [US6] Extend `ClientApp/src/features/admin/pages/AdminVoicePage.test.tsx`: a provider with `modelStatus: 'ModelUnavailable'` shows a "model unavailable" chip with the reason as a tooltip, and Preview surfaces the error toast.

### Implementation for User Story 6

- [X] T082 [US6] Create `src/AskLucy.Application/CustomModels/Abstractions/IHostedModelEngine.cs` and `IHostedModelLocator.cs`, with the `HostedModelResolution` union `NoRecord | Available(relativeDirectory, customModelId) | Unavailable(reason)`. Implement `src/AskLucy.Infrastructure/CustomModels/ScopedHostedModelLocator.cs` to pass T078, and register it as a singleton. It uses `FindCompletedForRepositoryAsync`, so only Completed records count (FR-039) and repository ids match ignoring case.
- [X] T083 [US6] Update `src/AskLucy.Infrastructure/Ai/Supertonic/SupertonicTextToSpeechEngine.cs` to implement `IHostedModelEngine` with `ModelRepositoryId = "Supertone/supertonic-3"`. Update `SupertonicModel.cs` to resolve its directory through the locator on each request:
  - Keep the loaded sessions keyed by directory, reloading under the existing `_initLock`.
  - Dispose the sessions on `Unavailable`.
  - Log each failure.
  - Confirm T079 passes. Watch for a DI cycle (locator → scope → repository must not depend on the engine).
- [X] T084 [US6] Add `ModelStatus` and `ModelStatusReason` to `src/AskLucy.Application/Ai/AdminVoiceProviderDto.cs`. Filter `IHostedModelEngine` engines in `src/AskLucy.Application/Ai/Queries/GetVoiceEngines/` and fill the status in `Queries/GetAdminVoiceProviders/` to pass T080.
- [X] T085 [US6] Update `ClientApp/src/features/admin/api/adminVoiceApi.ts` types, and show the "model unavailable" chip on each provider row in `ClientApp/src/features/admin/pages/AdminVoicePage.tsx`, to pass T081.
- [X] T086 [US6] Show a `backsEngine` chip ("Backs Supertonic") on custom model rows in `CustomModelsSection.tsx`. The summary DTO computes it from the registered `IHostedModelEngine`s.

**Checkpoint**: Deploy, make Available, add Supertonic from "+" and preview it. Make it Unavailable and confirm replies fail over. The existing manual install with no record keeps working.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T087 [P] Add a comment above the `SamKirkland/FTP-Deploy-Action` step in `.github/workflows/ci.yml`: custom-model folders (`Models/**`, `App_Data/Models/**`) survive only because `dangerous-clean-slate` is off. See specs/072 research D10.
- [X] T088 [P] Update the docs:
  - Architecture: the Custom Models module and the temporary deployment-target seam.
  - API: the new endpoints and hub.
  - Database: the two tables and their indexes.
  - Add an ADR, `docs/adr/00NN-custom-model-deployment-temporary-ftp-target.md`, recording that FTP-from-config is deliberately temporary until spec 071.
- [X] T089 [P] Add the new endpoints to any admin API catalogue or OpenAPI grouping (Swagger tags), and confirm `/openapi` generation still builds (Microsoft.OpenApi must stay on 2.x).
- [X] T090 Run `dotnet format --verify-no-changes` for the whole solution and fix any issues. Watch for `\r\r\n` and the migration BOM.
- [X] T091 Run `dotnet build "Ask Lucy.sln" -warnaserror` and every test project with `PERSISTENCE_TESTS_CONNECTION_STRING` set. Report every failure with its output. The three known `McpObservabilityTests` failures are pre-existing.
- [X] T092 In `ClientApp/`, run `npx tsc -b --noEmit`, `npx eslint .` and the **full** `npx vitest run`, and fix everything.
- [X] T093 Run [quickstart.md](quickstart.md) §2–§7 against production after deploying, following the numbered-steps screenshot loop.

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: needs Setup. Blocks every story.
- **US1 (Phase 3)**: needs Foundational. **This is the MVP.**
- **US2 (Phase 4)**: needs US1's job and controller (T039, T041).
- **US3 (Phase 5)**: needs US1's Hugging Face source and job (T036, T039). Can run **in parallel with US2**.
- **US4 (Phase 6)**: needs US1's uploader, source and job. Its UI parts need US2's section wiring (T055).
- **US5 (Phase 7)**: needs Foundational and US1's controller and section. Can run in parallel with US2–US4.
- **US6 (Phase 8)**: needs US5, because making a model Available needs T076. Its backend tests (T078–T080) can start after Foundational.
- **Polish (Phase 9)**: needs every story.

### Within each phase

Tests → Domain → Application → Infrastructure/Persistence → Web → frontend. Several tasks edit the same file and so run in sequence: `CustomModelDeploymentJob.cs` (T039 → T053 → T061 → T069), `AdminCustomModelsController.cs` (T041 → T054 → T076) and `CustomModelsSection.tsx` (T043 → T055 → T071 → T077 → T086).

---

## Parallel Examples

### Phase 2

```text
T007, T008, T009, T010   # Domain test files
T011                     # repository tests (Persistence.Tests)
T014, T015, T016         # enums and value objects (after their tests)
T021, T022, T023, T024   # Application seams, DTOs and audit log
T026, T027               # settings-provider tests and frontend API module
```

### User Story 1

```text
T028, T029, T030, T031, T032, T033, T034, T035   # all test files
T036 ∥ T037 ∥ T040                                # HF source, FTP uploader, queries
```

### User Stories 2 and 3 together

```text
Dev A: T045–T055 (progress, cancel, hub)
Dev B: T056–T061 (SSRF, traversal, cap)
```

---

## Implementation Strategy

### MVP first (User Story 1)

Phases 1–3 deliver a working server-side deployment, with list refresh instead of live updates. Validate it with quickstart §2 (the FTPS smoke test) **before** building further. If the host refuses TLS, `AllowPlainFtp` is a conscious decision for you to make.

### Incremental delivery

1. MVP (US1). Check that the FTPS smoke test passes.
2. US2 and US3: live progress and cancel, plus the guardrails. Don't enable this in production before US3 is done.
3. US4: failure hardening and the restart sweep.
4. US5: availability.
5. US6: the Supertonic binding. It's backward compatible, because with no record the engine behaves as before.
6. Polish: docs, the CI comment, the full gates, and the production quickstart.

## Notes

- Total: **93 tasks**.
- `[P]` means a different file with no incomplete dependency.
- Commit after each checkpoint, pushing directly to `main` as usual.
- Verify each commit actually contains your files, because a concurrent session may reset the tree.
