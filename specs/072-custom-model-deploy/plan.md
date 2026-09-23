# Implementation Plan: Custom Model Deployment (Admin)

**Branch**: `072-custom-model-deploy` | **Date**: 2026-09-23 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/072-custom-model-deploy/spec.md`

> ### ⚠️ Deliberately temporary: FTP settings come from configuration
>
> The deployment target, one FTP server, is read from the `Ftp` configuration section **on purpose,
> as a stop-gap** until the Connectors feature (spec 071) exists. All access to those settings goes
> through **one** Application interface, `IDeploymentTargetSettingsProvider`, which has **one**
> Infrastructure implementation, `ConfigurationDeploymentTargetSettingsProvider`. When Connectors
> lands, replacing that class and its DI line is the intended, planned change (SC-007). **A later
> refactor that removes `FtpOptions` or the `Ftp` section is the expected next step, not a design
> regression.** Nothing else in this feature (job, validators, DTOs, UI) may read `FtpOptions`
> directly. A review that finds such a reference should treat it as a defect.

## Summary

Admins add a server-hosted model from **Admin → AI Providers → Custom Models → Add model**. They
paste a Hugging Face model URL and a relative destination. The server records a `CustomModel` and
enqueues a Hangfire job, which runs these steps:

1. Pins the requested revision to a commit.
2. Lists the repository and enforces the size cap, path safety and reserved names before any byte
   moves.
3. For each file: streams it to server temp storage, checks its hash, uploads it over explicit-TLS
   FTP (FluentFTP) to `{RootPath}/{destination}/{path}`, records any overwrite, checks the remote
   size, and deletes the temp file.

Progress is pushed live over a permission-gated SignalR hub and persisted every 2 s. Cancelling
takes effect within 5 s. Every failure ends in a visible `Failed` state with a safe reason.

Once a deployment completes, the admin marks the model Available or Unavailable. The Supertonic
voice engine (spec 070) now loads from its Available custom model's folder, through a new
`IHostedModelLocator`. It is hidden from Voice "+" when its model is Unavailable, and the existing
voice router fails over. With no record, Supertonic keeps its configured folder (backward
compatible). Only a **Completed** record takes Supertonic off that folder, so a queued, running or
failed first deploy never silences a working voice (FR-039).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 / React 19 (ClientApp).

**Primary Dependencies**:
- Existing: MediatR, FluentValidation, EF Core (SQL Server), Hangfire 1.8.24, ASP.NET Core
  SignalR, MUI, TanStack Query, `@microsoft/signalr`.
- **New:** `FluentFTP` 55.0.0 (Infrastructure only).

**Storage**:
- SQL Server: new tables `CustomModels` and `CustomModelOverwrittenFiles` (one additive migration).
- Local disk: `App_Data/Temp/custom-models/{id}` while a job runs.
- Remote: the FTP server.

**Testing**: xUnit, NSubstitute, FluentAssertions and `FakeLogger` (backend); Vitest, React
Testing Library and MSW (frontend); `CustomWebApplicationFactory` for Web.Tests.

**Target Platform**: IIS on the site4now shared host (production). There the FTP root `/hydra` is
the app's content root.

**Project Type**: Web application (ASP.NET Core API plus the React SPA in `src/AskLucy.Web/ClientApp`).

**Performance Goals**:
- Guardrail refusals happen at submit or during Listing, before any byte moves (SC-002). There is
  no listing-time target.
- Progress pushed at least every 2 s (SC-004); cancel takes effect in 5 s or less (FR-023).
- Voice availability checked with one indexed lookup per voice request.

**Constraints**:
- The password never appears in logs, responses or exceptions (FR-017).
- Explicit TLS by default, with no silent fallback (FR-018a).
- Temp disk use peaks at the size of the largest single file.
- No `ValidateOnStart` on the new options (see the memory note on required options crashing the
  host).
- No retries for FTP writes.

**Scale/Scope**: A handful of models; single repositories up to 20 GB (the cap is configurable);
one active job per destination; one to three admins watching.

All unknowns are resolved in [research.md](research.md) (D1–D13). There are no remaining NEEDS
CLARIFICATION items.

## Constitution Check

*GATE: checked before Phase 0 and re-checked after Phase 1 design.*

| Principle | Status | How the design complies |
|---|---|---|
| **I. Clean Architecture (NON-NEGOTIABLE)** | ✅ | See the layer-by-layer notes below this table. |
| **II. SOLID** | ✅ | Narrow interfaces: settings, uploader, source, locator, notifier and cancellation registry. `IHostedModelEngine` is kept separate from `ITextToSpeechEngine` (ISP, research D9). |
| **III. Simplicity / YAGNI** | ✅ | No Connectors entity and no redeploy or delete (spec Assumptions). There is no locator cache, and one row holds both the model and its deployment. |
| **V. Dependency Inversion / Testability** | ✅ | The job depends only on Application interfaces, so FTP and Hugging Face failures can be unit-tested with fakes. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | ✅ | See the notes below this table. |
| **§3 Domain events** | ⚠️ Justified deviation | The job and handlers call `ICustomModelDeploymentNotifier` directly instead of raising domain events. This is always **after** a successful save. See Complexity Tracking. |
| **§3 Infrastructure isolation** | ✅ | Swapping FTP for Blob storage, or config for a connector, is a new Infrastructure class plus one DI line (SC-007). |
| **§5 Database** | ✅ | Code-first additive migration. Filtered unique indexes enforce FR-015, FR-032 and FR-034. Soft delete, RowVersion, and enums stored as strings. |
| **§6 API** | ✅ | `/api/v1/admin/...`, Problem Details, 202 for async work, offset paging on a small admin list (research D13), and `admin-endpoints` rate limiting. |
| **§8 Security** | ✅ | See the notes below this table. |
| **§10 Testing** | ✅ | Domain, Application, Infrastructure and Web tests are all planned, including the FTP-failure and traversal cases; see the Testing strategy. |
| **§13 Documentation** | ✅ | This plan, the contracts, and the updated example settings files. A comment in `ci.yml` records the deploy-safety dependency (research D10). |

**Principle I, layer by layer:**
- Domain holds the aggregate and two pure value objects, with no I/O.
- Application holds the commands, queries, job and interfaces. It has no `HttpClient`, EF Core or
  FluentFTP.
- Infrastructure holds FluentFTP, the Hugging Face `HttpClient`, the hub and the locator.
- Persistence holds the EF configuration and repository. Progress writes use `ExecuteUpdateAsync`
  there (see the memory note on Application never referencing EF Core).
- Controllers call only `ISender`.

**Principle VIII, how every failure surfaces:**
- Every job failure ends as a persisted `Failed` state with a reason, a log entry and a hub event,
  and the exception is then rethrown to Hangfire.
- A failed hub push is logged.
- The frontend handles every mutation with a toast, and connection loss shows an `isLive` banner.
- A server restart mid-job is surfaced as `Failed`, never left as a stall (FR-013).

**§8 Security, in detail:**
- SSRF has three layers: host allowlist parsing, a redirect allowlist, and a connect-time
  private-IP block (research D4).
- Paths are protected by an allowlist of destination prefixes, segment rules, and the
  `web.config`/`app_offline.htm` ban (research D5).
- TLS is required and certificates are validated.
- The password is redacted in `ToString()`.
- New permissions follow the existing admin scheme, and every action is audited (research D11,
  D12).

**Existing precedents reused, not new violations:**
- Application already references `Hangfire.Core` and enqueues through `IBackgroundJobClient`.
- The hubs live in Infrastructure.
- Structured `[LoggerMessage]` audit follows `AiAdminActionLog`.

**Post-design re-check (after Phase 1):** ✅ Passes, with one justified deviation (§3 domain events;
see Complexity Tracking). The design added the persisted `IsInProgress` column, the append-only
overwritten-files insert and the conditional terminal writes. All are Persistence-level bookkeeping
behind repository methods, and none breaks a principle.

## Project Structure

### Documentation (this feature)

```text
specs/072-custom-model-deploy/
├── plan.md               # This file
├── research.md           # D1–D13 decisions
├── data-model.md         # CustomModel aggregate, value objects, options, migration
├── quickstart.md         # Validation run-book
├── contracts/
│   ├── admin-custom-models.md          # REST, plus the Voice API delta
│   └── custom-model-deployment-hub.md  # SignalR events
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks (not created here)
```

### Source Code

```text
src/AskLucy.Domain/
├── Authorization/AdminPermissionCatalog.cs         # + AdminArea.CustomModels, custom-models.view/manage
└── CustomModels/
    ├── CustomModel.cs                               # aggregate and state machine
    ├── CustomModelOverwrittenFile.cs
    ├── CustomModelDeploymentState.cs · CustomModelAvailability.cs · CustomModelFailureKind.cs
    ├── HuggingFaceModelSource.cs                    # URL parsing (value object)
    └── DeploymentDestination.cs                     # path containment (value object)

src/AskLucy.Application/
├── Options/CustomModelsOptions.cs
├── Ai/
│   ├── AdminVoiceProviderDto.cs                     # + ModelStatus, ModelStatusReason
│   ├── Queries/GetVoiceEngines…Handler.cs           # hides engines whose model is Unavailable
│   └── Queries/GetVoiceProviders…Handler.cs         # fills ModelStatus
└── CustomModels/
    ├── Abstractions/
    │   ├── IDeploymentTargetSettingsProvider.cs     # ← the Connectors swap point
    │   ├── DeploymentTargetSettings.cs
    │   ├── IDeploymentFileUploader.cs               # OpenSessionAsync → IDeploymentUploadSession
    │   ├── DeploymentTargetException.cs             # Kind plus a safe message
    │   ├── IModelRepositorySource.cs                # ResolveRevision / ListFiles / Download
    │   ├── ModelRepositorySourceException.cs
    │   ├── ICustomModelRepository.cs                # incl. UpdateProgressAsync, AddOverwrittenFileAsync
    │   ├── ICustomModelDeploymentNotifier.cs
    │   ├── ICustomModelDeploymentCancellationRegistry.cs
    │   ├── IHostedModelEngine.cs
    │   └── IHostedModelLocator.cs                   # plus the HostedModelResolution union
    ├── Commands/
    │   ├── SubmitCustomModelDeployment/             # command, validator, handler
    │   ├── CancelCustomModelDeployment/
    │   ├── SetCustomModelAvailability/
    │   └── RemoveCustomModel/
    ├── Queries/
    │   ├── ListCustomModels/ · GetCustomModel/
    │   ├── GetDeploymentStatus/ · PreviewCustomModelSource/
    ├── Jobs/
    │   ├── ICustomModelDeploymentJob.cs
    │   └── CustomModelDeploymentJob.cs              # [AutomaticRetry(Attempts = 0)]
    ├── CustomModelDtos.cs
    └── CustomModelAdminActionLog.cs                 # [LoggerMessage] audit events

src/AskLucy.Infrastructure/
├── AskLucy.Infrastructure.csproj                    # + FluentFTP 55.0.0
├── DependencyInjection.cs                           # registrations; "HuggingFace" named HttpClient
├── Ai/Supertonic/
│   ├── SupertonicModel.cs                           # per-directory load, reload and dispose
│   └── SupertonicTextToSpeechEngine.cs              # + IHostedModelEngine
└── CustomModels/
    ├── Deployment/
    │   ├── FtpOptions.cs                            # TEMPORARY (see the note at the top)
    │   ├── ConfigurationDeploymentTargetSettingsProvider.cs   # TEMPORARY, the single swap point
    │   ├── FluentFtpDeploymentFileUploader.cs
    │   └── IFtpClientFacade.cs · AsyncFtpClientFacade.cs      # internal seam so the uploader is testable
    ├── HuggingFace/
    │   ├── HuggingFaceModelRepositorySource.cs
    │   ├── HuggingFaceRedirectPolicy.cs             # host allowlist, manual redirects
    │   └── SafeConnectCallback.cs                   # blocks private IPs at connect time
    ├── CustomModelDeploymentHub.cs                  # /hubs/custom-model-deployments
    ├── CustomModelDeploymentNotifier.cs
    ├── CustomModelDeploymentCancellationRegistry.cs
    ├── CustomModelDeploymentRecoveryHostedService.cs
    └── ScopedHostedModelLocator.cs

src/AskLucy.Persistence/
├── AskLucyDbContext.cs                              # + DbSet<CustomModel>
├── Configurations/CustomModelConfiguration.cs
├── Repositories/CustomModelRepository.cs
└── Migrations/<ts>_AddCustomModels.cs

src/AskLucy.Web/
├── Controllers/v1/AdminCustomModelsController.cs
├── Program.cs                                       # MapHub<CustomModelDeploymentHub>
├── appsettings.json                                 # + "CustomModels" defaults (no secrets)
├── appsettings.Development.json.example             # + "Ftp" placeholders
├── appsettings.Production.json.example              # NEW: "Ftp" placeholders, following the Development example
└── ClientApp/src/features/admin/
    ├── adminPermissions.ts · adminNav.tsx           # + custom-models permissions; AI Providers nav alternatives
    ├── api/adminCustomModelsApi.ts
    ├── hooks/useCustomModelDeploymentsHub.ts
    ├── components/customModels/
    │   ├── CustomModelsSection.tsx                  # list, progress, availability, remove, cancel
    │   ├── AddCustomModelDialog.tsx                 # Source, Destination, conditional Name
    │   ├── CustomModelProgress.tsx
    │   └── OverwrittenFilesDialog.tsx
    ├── pages/AdminAiProvidersPage.tsx               # permission-gated sections
    └── pages/AdminVoicePage (or its provider row)   # "model unavailable" chip

.github/workflows/ci.yml                             # comment only: dangerous-clean-slate must stay off

tests/
├── AskLucy.Domain.Tests/CustomModels/               # source parsing, destination, state machine
├── AskLucy.Application.Tests/CustomModels/          # handlers, validators, job (with fakes), voice handlers
├── AskLucy.Infrastructure.Tests/CustomModels/       # uploader error mapping, HF source, locator, Supertonic reload
├── AskLucy.Persistence.Tests/CustomModels/          # repository against real SQL Server: indexes, collations, forward-only progress
└── AskLucy.Web.Tests/CustomModels/                  # controller, permissions, redaction, hub access
```

**Structure Decision**: Feature folders named `CustomModels/` in each existing layer, matching how
`Workflows/`, `SiteAnalysis/` and similar features are laid out. Frontend work lives in
`features/admin`, because the section lives on the existing AI Providers page (clarification Q4).

## Key flows

1. **Submit** (`POST`):
   - The validator runs the Domain value objects.
   - The handler checks `IDeploymentTargetSettingsProvider` (400 if not configured), name uniqueness,
     and any active job at an equal or overlapping destination (FR-015, research D5).
   - It calls `CustomModel.Create`, saves through the unit of work, and enqueues
     `ICustomModelDeploymentJob.RunAsync(id)` via `IBackgroundJobClient`. It then stores the job id.
     Only after that save does it audit, notify, and return 202.
2. **Job**:
   1. Runs only from `Queued` (research D6), then moves to `StartListing`.
   2. Calls `ResolveRevision`, which fails on a gated or private repository. It stores the
      canonical repository id from the response (research D3 step 4).
   3. Calls `ListFiles`, which follows pagination. It checks every path through
      `DeploymentDestination.TryCombine`, checks the cap against the summed size, and checks disk
      space. Then it calls `BeginTransfer`.
   4. Opens an upload session. For each file:
      - Gets the remote size, and records an overwrite if the file exists.
      - Downloads to temp, verifying the hash.
      - Uploads, then verifies the remote size.
      - Deletes the temp file and persists or pushes progress. Each flush reads
        `GetRunSignalAsync` and stops on `CancellationRequested` or `NoLongerInProgress`.
   5. Calls `Complete`. Terminal writes reload the record and apply only if it is still in
      progress, retrying once on a RowVersion conflict (research D6).
   6. Wraps all of this in `try` / `catch` / `finally`:
      - `catch (OperationCanceledException)` when cancellation was requested → `MarkCancelled`.
      - `catch (Exception)` → `Fail(kind, safe reason)`, log, notify, rethrow.
      - `finally` → delete the temp folder and unregister the cancellation token source.
3. **Cancel** (`POST {id}/actions/cancel`): `RequestCancellation`, retried once on a RowVersion
   conflict. A `Queued` job is deleted from Hangfire; otherwise the registry's token source is
   cancelled.
4. **Availability**: `MakeAvailable` requires `Completed`. The handler checks for another Available
   model on the same repository (409 naming it), then audits and notifies. Supertonic's next request
   resolves the new state.

## Testing strategy

These cases are required. The user named the FTP-failure and traversal cases explicitly.

- **Domain**:
  - `HuggingFaceModelSource` theory table: valid tree, resolve and blob URLs, `refs/pr/N`,
    `www.`, and an ignored file path. Rejections: another host, userinfo trick, IP literal, a
    non-default port, datasets, spaces, `..` in owner or repo, an owner with no repo.
  - `DeploymentDestination` table: `..`, `%2e%2e`, double encoding, a leading `/`, `\`, `C:`,
    reserved device names, Unicode dots, a bare prefix, a prefix outside the allowlist.
  - `TryCombine` with `web.config` and `APP_OFFLINE.HTM` at any depth.
  - The full state-machine transition matrix, including illegal transitions.
- **Application** (fakes for source, uploader, repository, notifier and settings):
  - The happy path moves through the states in order.
  - **FTP authentication rejected → Failed(TargetAuthRejected)**, and the password is absent from
    the reason and from every captured log (`FakeLogger`).
  - **Connection lost mid-file → Failed, and the reason names the file.**
  - TLS not accepted, and plain FTP not allowed → Failed.
  - A remote size mismatch → Failed.
  - The size cap fails the job before any upload.
  - A traversal path from the repository listing fails the job before any upload.
  - A `web.config` in the listing fails the job.
  - Cancelling during a transfer → Cancelled and the temp folder deleted.
  - A job delivered in `Transferring` → Failed(InterruptedByRestart).
  - An overwrite is recorded and audited.
  - Validators; not-configured → 400; duplicate name, busy destination, or a second Available model
    → 409.
  - Overlapping destinations (`Models/a` against `Models/a/b`, any case) → 409 naming the model.
  - The job stores the canonical repository id from the revision response.
  - A record failed by the recovery sweep mid-run: the job stops at its next flush and never
    overwrites `Failed` with `Completed`.
  - The voice-engines query hides Supertonic when its Completed model is Unavailable. It shows it
    when there is no Completed record, including while a first deployment runs or after it fails.
- **Infrastructure**:
  - FluentFTP exception → `DeploymentTargetException` mapping. The message never contains the
    password.
  - `FtpOptions.ToString()` and `DeploymentTargetSettings.ToString()` are redacted.
  - The Hugging Face source:
    - A stubbed `HttpMessageHandler` covers tree pagination, the 302-to-CDN redirect being
      followed, and a redirect to a disallowed host being refused.
    - 429 with `Retry-After`, and a SHA-256 mismatch.
    - The stall watchdog.
  - `SupertonicModel`:
    - Resolves the directory for NoRecord, Available and Unavailable.
    - Reloads when the directory changes.
    - Unavailable throws `AiProviderUnavailableException`.
  - The recovery hosted service fails orphaned rows, including `Queued` rows with no live Hangfire
    job.
- **Persistence** (real SQL Server; skipped unless `PERSISTENCE_TESTS_DEDICATED_DATABASE=1`):
  - `UpdateProgressAsync` only moves forward.
  - The three filtered unique indexes, including case-insensitive `Name`, `RepositoryId` and
    `Destination`.
  - A unique violation becomes `DuplicateResourceException`.
  - `AddOverwrittenFileAsync`, `GetRunSignalAsync`, and the Completed-only locator query.
- **Web**:
  - Each endpoint returns 403 without the permission and 2xx with it.
  - A role holding only `admin.ai-providers.*` gets 403.
  - Submitting a traversal destination returns 400 Problem Details with `errors.destination`.
  - The `deployment-status` body contains no host, username, password or root path.
  - With a sentinel password, no response body or header, and no persisted `CustomModels` or
    `CustomModelOverwrittenFiles` value, contains it (SC-006).
  - With the `Ftp` section removed, the host still boots, the status reports `isConfigured: false`,
    and submitting returns 400 `deployment-not-configured`.
  - Hub access: a connection without the permission is rejected.
- **Frontend (Vitest)**:
  - The dialog shows the Name field only when the name can't be derived or is taken.
  - Server validation errors show inline.
  - Progress events update the row.
  - The `isLive` banner shows.
  - A view-only user sees no manage controls, and the providers-table query is disabled without
    `ai-providers.view`.
  - The Voice page's "model unavailable" chip.
  - Every mutation has an error toast.
  - Axe accessibility tests (constitution §10) for `AddCustomModelDialog`, `CustomModelsSection`
    and `OverwrittenFilesDialog`.

## Risks

| Risk | Mitigation |
|---|---|
| The host refuses AUTH TLS (CI uses plain `ftp://`) | Quickstart §2 smoke test. `AllowPlainFtp` is an explicit opt-in that logs a warning (research D2). |
| Overwriting ONNX files the running Supertonic has loaded may be refused (550, file locked) | The job fails with `TargetWriteRejected`, naming the file. Guidance: make the model Unavailable first, which disposes the sessions (research D9), then redeploy. |
| Shared-host disk quota versus the 20 GB cap | Only one file is held in temp at a time, and free space is checked before each file (research D6). |
| The dev config points at the production FTP server | The job writes where it's told. Quickstart runs deployments from the production admin panel (spec Assumption). A dev job would write real files to production, so this is documented, not blocked. |
| The CI deploy deleting model files | Verified safe (research D10). A comment in `ci.yml` guards against `dangerous-clean-slate`. |
| An app-pool recycle kills a long job | It surfaces as Failed(InterruptedByRestart). There is no auto-retry, by design (FR-014). |
| IIS overlapped recycle: the new worker's sweep fails a job the old worker is still finishing | Terminal writes are conditional, so the record stays Failed. The old worker's next flush sees `NoLongerInProgress` and stops (research D6, D7). It can cost a nearly finished deploy; disable overlapped recycle on the app pool if site4now allows it. |
| Progress `UPDATE`s bump RowVersion and race aggregate saves | Cancel, the sweep and the job's transitions reload and retry once. A second conflict surfaces as a failure (research D6). |

## Complexity Tracking

| Deviation | Why it's needed | Simpler alternative rejected because |
|---|---|---|
| **§3 Domain events**: the submit, cancel, availability and remove handlers and the deployment job call `ICustomModelDeploymentNotifier` (a SignalR push) and write audit events directly, instead of raising domain events from `CustomModel` that are dispatched after commit. | The codebase has no domain-event infrastructure: no aggregate event collection and no post-commit dispatcher. The nearest precedent is `CancelWorkflowExecutionCommandHandler`, which calls `IWorkflowExecutionNotifier` right after `SaveChangesAsync`. The only reactors here are a UI projection (the hub) and the audit log; no other module reacts. The voice engine *pulls* state through `IHostedModelLocator` on each request (research D9), so it needs no event. | Building aggregate event collection plus a post-commit dispatcher for one feature is platform work that belongs in its own spec. It would touch every aggregate's save path. |

**The constraints that keep this safe:**
- Notifier and audit calls happen only **after** a successful save. Tasks T028 and T046 test the
  ordering: nothing is pushed when the save throws.
- The job notifies from its own flush and terminal steps, which persist first.
- If another module later needs to react to a custom model changing, for example the capabilities
  redesign or spec 071, publish a MediatR `INotification` after the save. That follows
  `UploadDocumentCommandHandler`'s `DocumentUploadedNotification`. Don't add another direct call.

The **temporary FTP-from-config design** (see the note at the top) is not a violation. It's a
scoped, deliberate simplification that sits behind the interface Principle I requires.
