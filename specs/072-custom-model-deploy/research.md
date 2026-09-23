# Research: Custom Model Deployment (Admin)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-09-23

Each decision lists what was chosen, why, and the alternatives that were rejected. Facts about
Hugging Face were checked against the live API on 2026-09-23 using `Supertone/supertonic-3`.

---

## D1 — Deployment target settings: read from configuration, behind one replaceable interface

**Decision.** The Application layer defines `IDeploymentTargetSettingsProvider`, with
`ValueTask<DeploymentTargetSettings?> GetAsync(CancellationToken)`. A null result means "not
configured". Its only implementation, `ConfigurationDeploymentTargetSettingsProvider`
(Infrastructure), reads `IOptionsMonitor<FtpOptions>` bound from the `Ftp` section. No other type
references `FtpOptions` or the `Ftp` section.

**This is deliberately temporary.** Spec 071 (Deployment Connectors) will replace this provider
with one that reads a connector record. That swap is one new class plus one DI line. A later
refactor of this seam is the planned path, not a design regression.

- `FtpOptions`: `Host`, `Port` (default 21), `Username`, `Password`, `RootPath`, and
  `AllowPlainFtp` (default `false`). It is a `sealed record` with **`PrintMembers` overridden** so
  the compiler-generated `ToString()` never prints `Password`. `DeploymentTargetSettings` does the
  same. A unit test asserts that `ToString()` on both types never contains the password.
- **No `ValidateOnStart()`.** A required option with no default fails the whole host at boot
  (see the memory note on `ValidateOnStart`). Instead, missing or blank Host, Username or RootPath
  makes the provider return null, and the feature reports "deployment not configured" (FR-019).
- `IOptionsMonitor` rather than `IOptions`, so an edited `appsettings.Production.json` takes effect
  without a restart.

**Rejected.**
- Injecting `IOptions<FtpOptions>` directly into the job. The FTP section's shape would then leak
  into Application, and the connector swap would touch the job, the validator and the status
  query.
- Building the connector entity now. The user explicitly deferred it.

## D2 — FTP client: FluentFTP `AsyncFtpClient`

**Decision.** Add `FluentFTP` 55.0.0 (the current stable release on nuget.org) to
`AskLucy.Infrastructure`. The code uses `AsyncFtpClient` only. `FtpWebRequest` is obsolete
(SYSLIB0014).

- **Encryption (FR-018a):** `EncryptionMode = Explicit`. If `AllowPlainFtp = true`, use
  `EncryptionMode = None`. **Never `Auto`**, because Auto falls back to plain FTP silently, which the
  spec forbids.
- **Certificates:** `ValidateAnyCertificate = false`. The `ValidateCertificate` event accepts only
  `SslPolicyErrors.None`, so an invalid certificate fails the job with "the deployment server's TLS
  certificate is not trusted".
- **Uploads:** `UploadFile(local, remote, FtpRemoteExists.Overwrite, createRemoteDir: true,
  FtpVerify.None, progress)`. FluentFTP creates missing remote directories itself. FR-011 then
  compares the remote size (`GetFileSize`) with the expected size. FluentFTP's own verify option is
  not used, because it depends on server-side hash support.
- **Overwrite detection (FR-010a):** call `GetFileSize(remotePath)` before each upload. A value of
  `-1` means the file does not exist. Otherwise the value is recorded as the previous size.
- **Session:** one connection per job, reused for every file. The session reconnects once if the
  control connection drops *between* files. A drop *during* a file fails the job, and the reason
  names that file (User Story 4, scenario 2).
- **Error mapping:** FluentFTP exceptions (`FtpAuthenticationException`, `FtpCommandException`
  with 530, `AuthenticationException`, `IOException`, `TimeoutException`) are translated into
  `DeploymentTargetException(DeploymentTargetFailureKind, safeMessage)`. The original exception is
  logged as the inner exception, not as the message, and the **password is never** put into any
  message. FluentFTP's own logger is left unattached, because its verbose log includes the `PASS`
  command.

**Rejected.** `FtpWebRequest` (obsolete, and needs a new connection per file). SSH.NET (this server
speaks FTP, not SFTP). Keeping `EncryptionMode.Auto` for convenience (it allows a silent plain-FTP
fallback).

**Risk to check before go-live.** The CI deploy (`.github/workflows/ci.yml`) reaches this host
over plain `ftp://`. Run quickstart §2 against production first. If the host refuses AUTH TLS, set
`Ftp:AllowPlainFtp: true` in the untracked `appsettings.Production.json`. That is a conscious,
visible downgrade, and it is logged as a warning at the start of every job.

## D3 — Hugging Face: pin to a commit, list files, stream per file

**Verified facts.**
- `GET https://huggingface.co/api/models/{owner}/{repo}/revision/{rev}` returns `sha` (the commit),
  `private` and `gated`.
- `GET https://huggingface.co/api/models/{owner}/{repo}/tree/{rev}?recursive=1` returns entries
  with `type`, `path` and `size`, plus `oid` (the git blob SHA-1). LFS files also carry
  `lfs.oid` (SHA-256) and `lfs.size`. Supertonic 3 has 38 files totalling 415 MB. Large repositories
  are paginated through a `Link: <…>; rel="next"` header, which the client follows.
- `GET https://huggingface.co/{owner}/{repo}/resolve/{sha}/{path}` answers **302** to a CDN host.
  The one observed was `us.aws.cdn.hf.co`. Hugging Face also uses `cdn-lfs*.hf.co`,
  `cas-bridge.xethub.hf.co` and similar hosts.

**Decision.**
1. Resolve the requested revision (or `main` if none) to its commit `sha` once. List files and
   download everything by that `sha`, so all files come from one consistent commit even if the
   branch moves mid-job. The `sha` is stored on the record as `ResolvedCommitSha`.
2. Refuse a private or gated repository before downloading anything: "repository is private or
   gated; this server has no Hugging Face access token" (spec Assumption).
3. Check integrity after download. LFS files are compared against `lfs.oid` (SHA-256). Other files
   are compared against the git blob SHA-1 (`sha1("blob {size}\0" + bytes)`). A mismatch fails the
   job. This is extra protection beyond the spec's size check, and it costs one hashing pass while
   the file streams in.
4. **Canonicalise the repository id (FR-033).** Hugging Face repository URLs are case-insensitive,
   so an admin could type `supertone/Supertonic-3`. The job stores the `id` field from the revision
   response as `RepositoryId`, replacing the typed casing. `RepositoryId` also uses a
   case-insensitive collation (data-model), so the engine binding and the one-Available-per-repository
   rule (FR-034) still hold if the response ever omits `id`. Confirm the field in T031's fixture,
   captured from the live API.

**Rejected.** Downloading from `/resolve/main/...` for every file (the branch could move
mid-job). Using `huggingface_hub` or the CLI (Python dependency). Snapshot zip downloads (Hugging
Face doesn't offer them for model repositories).

## D4 — SSRF containment for the outbound fetch

**Decision.** There are three layers:
1. **Parsing the source (Domain `HuggingFaceModelSource.TryParse`).** It uses
   `Uri.TryCreate(Absolute)` and requires scheme `http` or `https`. The host, lowercased, must be
   exactly `huggingface.co` or `www.huggingface.co`. `UserInfo` must be empty, and the port must be
   the scheme's default. IP literals are rejected. The path grammar is
   `/{owner}/{repo}[/(tree|resolve|blob)/{revision}[/{file…}]]`. `datasets` and `spaces` in the
   first segment are rejected ("not a model repository"). A revision whose first segment is `refs`
   takes three segments (`refs/pr/3`). Otherwise it takes one. Owner and repo must match
   `^[A-Za-z0-9][A-Za-z0-9._-]{0,95}$` and must not be `.` or `..`. **The URL the admin typed is
   never fetched.** The server builds its own URLs from the parsed `owner/repo/sha`, and path parts
   are escaped with `Uri.EscapeDataString`.
2. **Redirect allowlist.** The named HttpClient `HuggingFace` has `AllowAutoRedirect = false`. The
   source follows at most 5 redirects itself. Each target must be `https`, and its host must be
   `huggingface.co` or end in `.huggingface.co` or `.hf.co`. Any other host fails the job with
   "Hugging Face redirected to an unexpected host" (FR-003).
3. **Connect-time address check.** `SocketsHttpHandler.ConnectCallback` resolves DNS itself and
   refuses loopback, private (RFC 1918 / ULA), link-local and unspecified addresses. This protects
   against DNS rebinding of an allowed host name.

**Rejected.** Allowing arbitrary hosts with a private-IP check only (the spec requires Hugging Face
only). Following redirects automatically (the redirect target would never be checked).

## D5 — Destination and path containment

**Decision.**
- **Domain `DeploymentDestination.TryCreate(raw)`** first percent-decodes the input once, and
  rejects any input that decodes further (double encoding). It normalises `/` separators and
  collapses repeated slashes. It then rejects: an empty result; a leading `/` or `\`; any `\`; a
  drive letter (`X:`); control characters; segments that are `.` or `..`; segments ending in `.` or
  a space; Unicode look-alike dots (U+2024, U+FF0E, U+3002); the characters ``<>:"|?*``; and Windows
  reserved device names (`CON`, `NUL`, `COM1`, and so on). It stores the canonical form
  `Models/supertonic-3`.
- **Reserved locations (FR-005a) use an allowlist, not a denylist.**
  `CustomModels:AllowedDestinationPrefixes` defaults to `["Models", "App_Data/Models"]`. The
  destination must be *strictly below* one of these (`Models/x` is accepted, `Models` alone is
  not). On production the deployment root `/hydra` is the application's content root. A denylist of
  the application's own folders (`wwwroot`, `runtimes`, and so on) would be incomplete and would
  drift every time the publish output changed. `App_Data/Models` stays allowed so Supertonic's
  existing default folder (`App_Data/Models/supertonic-3`) can be deployed to in place.
- **Repository file paths (FR-006)** are checked with the same segment rules via
  `DeploymentDestination.Combine(relativePath)`. Any file whose name is `web.config` or
  `app_offline.htm` at any depth, ignoring case, fails the job before any transfer. The check runs
  on the complete list, before the first byte.
- **Overlapping destinations (FR-015).** The submit handler refuses a destination when an
  in-progress job's destination is equal to it, or when either one is a prefix of the other on a
  segment boundary, ignoring case (`Models/a` against `Models/a/b`; `Models/ab` is not a prefix of
  `Models/a`). The 409 names the conflicting model. `Destination` uses a case-insensitive collation,
  so the filtered unique index backstops the equal-path race. The overlapping-path race has no
  database backstop, which is acceptable for an admin-only action with a handful of admins.
- **The remote path** is `{RootPath.TrimEnd('/')}/{destination}/{relativePath}`. The root path is
  only ever joined inside the FTP adapter. Records, DTOs and progress events hold only the relative
  destination and file paths (FR-018, and the spec's Key Entities).

## D6 — Background execution: a Hangfire job with no retries, processing one file at a time

**Decision.**
- `CustomModelDeploymentJob` (Application) is enqueued via `IBackgroundJobClient`. This is the
  existing pattern: `DocumentProcessingPipeline` and `PasswordResetIssuanceJob` also enqueue through
  interfaces, and Application already references Hangfire's client.
  `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` enforces
  FR-014.
- **Guard against running twice.** `RunAsync` proceeds only when the record is `Queued`. If Hangfire
  re-delivers the job after a crash (SQL storage requeues aborted jobs), the record will be
  `Listing` or `Transferring`. The job then marks it `Failed` ("interrupted by a server restart"),
  cleans up, and returns. It never re-runs silently. A record that is already `Cancelled` or
  `Failed` produces a log line and an immediate return.
- **Startup sweep.** `CustomModelDeploymentRecoveryHostedService` fails any record still in
  `Listing` or `Transferring` at boot, and deletes its temporary folder (FR-013). It also fails a
  `Queued` record whose Hangfire job is missing, or is in no live state (not Enqueued, Scheduled or
  Processing), so a lost enqueue never leaves a row `Queued` forever. It uses its own
  `IServiceScopeFactory` scope.
- **Terminal writes are conditional.** Every terminal transition (`Complete`, `Fail`,
  `MarkCancelled`) reloads the record in a fresh scope and applies only if the record is still in
  progress. A record the sweep already failed is never flipped back to `Completed`.
- **RowVersion and bookkeeping writes.** `UpdateProgressAsync` is a plain `UPDATE`, so it bumps
  the SQL Server `rowversion`. Any aggregate save that can race it (the cancel command, the sweep,
  and the job's own state transitions) reloads and retries **once** on a concurrency conflict,
  re-evaluating the transition against the fresh state. A second conflict is surfaced as a failure,
  not swallowed. Application catches a generic `Exception` for this, never an EF type (see the
  memory note on Application never referencing EF Core).
- **IIS overlapped recycle.** During an overlapped recycle, the new worker's startup sweep can fail
  a job the old worker is still finishing. The conditional terminal write above keeps the record
  `Failed(InterruptedByRestart)`. The old worker's next flush sees the record is no longer in
  progress (D7) and stops the transfer. The outcome is visible and matches FR-013. It can cost a
  deploy that was about to finish, and disabling overlapped recycle on the app pool avoids it, if
  site4now allows that setting.
- **Per-file pipeline.** For each file: download it to temporary storage, verify its hash, upload
  it, verify its remote size, then **delete the temporary file**. Peak temporary disk use is
  therefore the largest single file, not the whole repository. That matters on the shared
  site4now host, where disk quota is limited.
- **Temporary storage.** `{ContentRoot}/App_Data/Temp/custom-models/{deploymentId}/`, configurable
  through `CustomModels:TempDirectory`. It is deleted in `finally` and swept at startup. The CI FTP
  sync never removes it (see D10). Before each file, `DriveInfo.AvailableFreeSpace` is checked
  against the file size plus a 256 MB margin. If there isn't enough space, the job fails with
  "not enough free disk space on the server". An `IOException` with a disk-full `HResult` during a
  write fails the job with the same reason.
- **Stall watchdog.** Downloads use `HttpCompletionOption.ResponseHeadersRead`, so
  `HttpClient.Timeout` (60 s, following the short-timeout memory note) covers only the headers. A
  read-idle watchdog cancels a download that receives no bytes for 60 s, and the job fails with
  "download stalled". FTP uses FluentFTP's `ReadTimeout` and `DataConnectionReadTimeout`, set to
  60 s. Neither side can hang forever.
- **Hugging Face retries.** Metadata calls and each file download retry up to 3 times on 429 or
  5xx, with backoff of 2, 8 and 30 s, honouring `Retry-After` up to a 60 s cap. After that the job
  fails with the upstream status. FTP transfers are **not** retried, because a partial write to
  production should be visible, not hidden.

**Rejected.** An in-memory `Channel` with a hosted consumer (lost on restart, no dashboard).
Downloading the whole repository before uploading (needs disk space for the whole repository).

## D7 — Cancellation within 5 seconds (FR-023)

**Decision.** Cancellation is signalled two ways:
1. `ICustomModelDeploymentCancellationRegistry`, an Infrastructure singleton holding a
   `ConcurrentDictionary<Guid, CancellationTokenSource>`. The job registers its source.
   `CancelCustomModelDeploymentCommand` signals it immediately.
2. `CancellationRequestedAtUtc` is persisted on the record. The job's progress flush, which runs at
   most every 2 s, reads it back through `ICustomModelRepository.GetRunSignalAsync`. That returns
   `Continue`, `CancellationRequested`, or `NoLongerInProgress` (the sweep failed the record; see
   D6). The second covers the command and the job running in different processes. The third stops
   a job orphaned by an overlapped recycle.

The job links the Hangfire token, the registry token and the watchdog. On cancellation it deletes
the temporary folder, marks the record `Cancelled`, and pushes a final event. A `Queued` job is
marked `Cancelled` directly by the command, and its Hangfire job is deleted. The job's guard (D6)
exits if the job is ever delivered anyway.

**Rejected.** Relying on Hangfire's `BackgroundJob.Delete` → cancellation token alone. Its
server-side watcher polls on Hangfire's own interval, so it can't guarantee the 5-second bound.

## D8 — Live progress: a permission-gated SignalR hub, throttled

**Decision.** `CustomModelDeploymentHub` (Infrastructure, mapped at
`/hubs/custom-model-deployments`) follows `SiteAnalysisHub` and `DocumentProcessingHub`. The
difference is group membership. On connect, the hub resolves the caller's effective permissions
through `IEffectivePermissionResolver`. Only callers holding `admin.custom-models.view` join the
`custom-model-viewers` group. Other callers are aborted with a `HubException` (FR-026). The
existing hubs gate admin groups by role name. That would wrongly exclude custom roles that have
been granted the permission.

- `ICustomModelDeploymentNotifier` (Application) is implemented by `CustomModelDeploymentNotifier`
  (Infrastructure, `IHubContext<…>`).
- **Throttling:** progress events are sent at most every 500 ms per job, and always at file
  boundaries and state changes. SC-004 requires at least every 2 s.
- **Persisted progress:** `TransferredBytes`, `CompletedFileCount`, `CurrentFilePath` and
  `CurrentFileBytes` are written at most every 2 s through
  `ICustomModelRepository.UpdateProgressAsync`, using `ExecuteUpdateAsync` in Persistence. This is a
  monotonic bookkeeping write that doesn't touch the tracked entity, following the memory note that
  Application never references EF Core. A page reload therefore shows progress no more than 2 s
  old (FR-021).
- **Delivery failures:** a failed hub push is logged and does not stop the transfer. The client
  shows its own connection state (FR-024) and recovers by refetching over REST on reconnect.
- **Frontend:** the `useCustomModelDeploymentsHub` hook follows `useSiteAnalysisHub`: an
  `isAuthenticated` boolean dependency, `withAutomaticReconnect`, an `isLive` flag, and a query
  invalidation on `onreconnected`. The cookie-delivered access token (see the SignalR frozen-token
  memory note) needs no `accessTokenFactory`.

**Rejected.** Polling (the user excluded it). Reusing `DocumentProcessingHub`'s admin group (it is
role-gated and belongs to a different module).

## D9 — Connecting Supertonic to its custom model (FR-033 to FR-039)

**Decision.**
- A new narrow Application interface, `IHostedModelEngine { string ModelRepositoryId { get; } }`,
  is implemented by `SupertonicTextToSpeechEngine` (`"Supertone/supertonic-3"`). Keeping it
  separate from `ITextToSpeechEngine` (ISP) means the future capabilities work can apply it to
  Whisper or any other hosted engine without changing the voice interface.
- A new Application interface, `IHostedModelLocator`, has
  `Task<HostedModelResolution> ResolveAsync(string repositoryId, CancellationToken)`. Only
  **Completed**, non-deleted records count (FR-039, clarification H1), and the repository id is
  compared ignoring case (D3 step 4). The results are:
  - `NoRecord` when no Completed record exists, including while a first deployment is Queued,
    running, Failed or Cancelled. The engine keeps its configured folder.
  - `Available(relativeDirectory, customModelId)` when a Completed record is Available.
  - `Unavailable(reason)` when Completed records exist but none is Available.
  - Its Infrastructure implementation creates **its own DI scope** for each call. It is called from
    the singleton `SupertonicModel` and from voice requests running alongside other scoped work.
    This follows the memory notes on sharing a DbContext across concurrent tasks and
    `ScopeIsolatedLocationResolutionService`.
  - There is **no cache**. The call is one indexed lookup per voice request (not per chunk). That
    satisfies FR-038 ("the next voice request") without any cache-invalidation plumbing.
- `SupertonicModel` changes:
  - Each call resolves the directory through the locator: `NoRecord` gives
    `SupertonicOptions.ModelDirectory`; `Available` gives `{ContentRoot}/{destination}`.
  - The loaded ONNX sessions and voice-style cache are keyed by directory. If the directory changes,
    the old sessions are disposed and the new ones loaded, under the existing `_initLock`.
  - On `Unavailable`, the loaded sessions are disposed (freeing about 450 MB), an
    `AiProviderUnavailableException("The Supertonic model is marked unavailable in Custom Models.")`
    is thrown, and the failure is logged. The existing `VoiceProviderRouter` failover (spec 070
    FR-004) and its `VoiceProviderFailoverEvent` logging handle the rest. No router change is
    needed.
  - If a file is missing under a resolved `Available` directory, the same exception is thrown with
    "model files not found on this server".
- `GetVoiceEnginesQueryHandler` hides an `IHostedModelEngine` when resolution is `Unavailable`
  (FR-036). `NoRecord` and `Available` are both shown.
- `AdminVoiceProviderDto` gains `modelStatus` (`"Ready"` or `"ModelUnavailable"`) and
  `modelStatusReason`, filled by the same locator. The Voice page shows a chip. Preview already
  shows the server's reason (spec 070 User Story 1, scenario 4).
- **One Available model per repository (FR-034)** is enforced twice: by the command, which checks
  and returns 409 naming the model in use, and by a filtered unique index in the database, which
  turns a race into a 409 through the existing `DbUpdateException` → conflict mapping.

**Rejected.** A property on `ITextToSpeechEngine` (a fat interface; ElevenLabs would carry a
meaningless null). Caching resolutions with invalidation (complexity with no measured need).
Deleting the Supertonic `VoiceProvider` row when its model goes Unavailable (the spec says keep the
row).

## D10 — The CI app deploy must not delete deployed models

**Finding.** `.github/workflows/ci.yml` deploys with `SamKirkland/FTP-Deploy-Action@v4.4.0`, from
`local-dir: publish/backend/` to `server-dir: /hydra/`, without `dangerous-clean-slate`. That
action keeps `.ftp-deploy-sync-state.json` on the server and deletes **only** files recorded in its
own previous sync state that are no longer in the local folder. Files it never uploaded, such as
`Models/**`, `App_Data/Models/**` and `App_Data/Temp/**`, are not in that state and are never
deleted. The publish output has no `Models/` folder (model files are outside git; spec 070
Decision 7).

**Decision.** No workflow change is needed. Add a comment beside the step saying that
custom-model folders depend on `dangerous-clean-slate` staying off. The deploy's
`app_offline.htm` step stops the application, so a running job is interrupted and ends `Failed`
through D6, never silently.

## D11 — Permissions and page placement

**Decision.**
- `AdminArea.CustomModels` gets `admin.custom-models.view` and `admin.custom-models.manage` in
  `AdminPermissionCatalog`. `PermissionCatalogReconciler` picks them up at startup. Built-in roles
  get them through `PermissionSet.Full`. The frontend mirror (`adminPermissions.ts`) is extended to
  match.
- `/admin/ai-providers` nav permission becomes
  `[ai-providers.view, default-models.view, ai-capabilities.view, custom-models.view]`.
  `AdminAiProvidersPage` renders the providers table only with `admin.ai-providers.view`: its query
  gets `enabled: hasPermission(...)`, so a custom-models-only admin never receives a 403 toast. It
  renders `CustomModelsSection` only with `admin.custom-models.view`.

## D12 — Audit

**Decision.** Follow the existing admin precedent (`AiAdminActionLog` and `AdminActionLog`): a
`CustomModelAdminActionLog` with `[LoggerMessage]` structured security events for submit, cancel,
complete, fail, availability change, removal and **each overwrite** (FR-010a, FR-027). Each event
carries the actor's user id, the custom model id and the relative paths. It never carries the
password or root path. The persisted record also keeps who and when (`CreatedBy`, `ModifiedBy`,
`SubmittedByUserId`) and the list of overwritten files, which gives an in-database trail the admin
can see.

**Testing note.** Log assertions use `FakeLogger` (Microsoft.Extensions.Diagnostics.Testing). They
do not use `Received().Log(...)`, which never matches source-generated logging (see the NSubstitute
`[LoggerMessage]` memory note).

## D13 — Lists and pagination

**Decision.** `GET /api/v1/admin/custom-models` is offset-paginated (`page`, `pageSize` default 50,
max 100), sorted by `CreatedAtUtc` descending. Constitution §6 allows offset pagination for small,
stable admin lists. The overwritten-files list is returned by the detail endpoint, and is itself
paginated (default 100, max 500), because a large repository could overwrite thousands of files.
