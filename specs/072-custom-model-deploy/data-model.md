# Data Model: Custom Model Deployment (Admin)

**Spec**: [spec.md](spec.md) · **Research**: [research.md](research.md)

One new aggregate (`CustomModel`) with one owned child collection, two Domain value objects, and
configuration-only settings. No existing table changes shape.

---

## Aggregate: `CustomModel` (Domain — `AskLucy.Domain/CustomModels/CustomModel.cs`)

Inherits `BaseEntity` (Id, CreatedAtUtc/By, ModifiedAtUtc/By, DeletedAtUtc/By soft delete,
RowVersion). Table `CustomModels`. The model and its single deployment share one row: the spec rules
out redeploying, so one model has exactly one deployment (spec Assumptions).

| Field | Type | Rules |
|---|---|---|
| `Name` | string(100) | Required. Unique among non-deleted rows, **ignoring case** (FR-032). Derived from the repository name unless the admin supplied one (FR-002). |
| `RepositoryId` | string(200) | `owner/repo` as Hugging Face canonicalises it: the job replaces the typed casing with the `id` from the revision response (research D3 step 4). Collation `SQL_Latin1_General_CP1_CI_AS`, so every comparison ignores case. Used for engine binding (FR-033). |
| `Revision` | string(255) | What the admin asked for (`main` when omitted). |
| `ResolvedCommitSha` | string(40)? | The 40-hex commit every file was fetched from (research D3). Null until Listing succeeds. |
| `SourceUrl` | string(2048) | The URL as submitted, for display only. **Never fetched** (research D4). |
| `Destination` | string(512) | Canonical relative path, for example `Models/supertonic-3` (research D5). Never contains the root path. Collation `SQL_Latin1_General_CP1_CI_AS`. |
| `DeploymentState` | enum → string(20) | `Queued`, `Listing`, `Transferring`, `Completed`, `Failed`, `Cancelled` (FR-022). |
| `Availability` | enum → string(20) | `Available` or `Unavailable`. Starts `Unavailable` (FR-030). |
| `TotalBytes` | long? | Sum of the repository's file sizes. Set when Listing completes. |
| `TransferredBytes` | long | Bytes confirmed uploaded, plus bytes of the current file uploaded so far. |
| `TotalFileCount` | int? | Set when Listing completes. |
| `CompletedFileCount` | int | Files downloaded, uploaded and verified. |
| `CurrentFilePath` | string(1024)? | Relative path within the repository. Null outside `Transferring`. |
| `CurrentFileBytes` / `CurrentFileTotalBytes` | long? | Progress of the file in flight. |
| `FailureReason` | string(1000)? | Safe, human-readable text. Never contains the password or root path (FR-017, FR-018). |
| `FailureKind` | enum → string(40)? | `SourceNotFound`, `SourceUnavailable`, `SourceGatedOrPrivate`, `SizeLimitExceeded`, `UnsafeRepositoryPath`, `ReservedFileName`, `IntegrityMismatch`, `DownloadStalled`, `DiskSpaceExhausted`, `TargetNotConfigured`, `TargetAuthRejected`, `TargetTlsNotAccepted`, `TargetCertificateInvalid`, `TargetConnectionLost`, `TargetWriteRejected`, `TargetSizeMismatch`, `InterruptedByRestart`, `Unexpected`. |
| `SubmittedByUserId` | Guid | The admin who pressed Add model. |
| `CancellationRequestedAtUtc` | DateTimeOffset? | Set by the cancel command. The job reads it on each flush (research D7). |
| `CancelledByUserId` | Guid? | |
| `BackgroundJobId` | string(100)? | The Hangfire job id, so a cancelled `Queued` job can be deleted. |
| `StartedAtUtc` / `FinishedAtUtc` | DateTimeOffset? | `StartedAtUtc` is set when Listing begins; `FinishedAtUtc` when the job reaches any terminal state. |
| `IsInProgress` | bool (persisted) | True for `Queued`, `Listing` and `Transferring`. Stored as a real column so the filtered unique index below can use it. It is kept in step by the Domain methods, never set directly. |
| `OverwrittenFileCount` | int | Denormalised count, so the list doesn't need to join the child table. |

### Owned collection: `CustomModelOverwrittenFile` (table `CustomModelOverwrittenFiles`)

| Field | Type | Rules |
|---|---|---|
| `Id` | long identity | |
| `CustomModelId` | Guid FK | Cascade delete. |
| `RelativePath` | string(1024) | The path relative to the **root**, for example `Models/supertonic-3/onnx/vocoder.onnx` (FR-010a). |
| `PreviousSizeBytes` | long | The remote size before the overwrite. |
| `OverwrittenAtUtc` | DateTimeOffset | |

Rows are **appended directly** by the job through
`ICustomModelRepository.AddOverwrittenFileAsync` (one `INSERT`, in its own scope), not by loading
the aggregate. A repository with thousands of overwritten files would make the aggregate expensive
to load. The Domain method `RecordOverwrite` still owns the rule (only allowed while
`Transferring`); the job calls it on its in-memory copy and then persists the row.

### Indexes (Persistence — `Configurations/CustomModelConfiguration.cs`)

| Index | Filter | Purpose |
|---|---|---|
| `UX_CustomModels_Name` on `Name` | `DeletedAtUtc IS NULL` | FR-032. The column uses collation `SQL_Latin1_General_CP1_CI_AS`, so uniqueness ignores case even if the database default changes. |
| `UX_CustomModels_Repository_Available` on `RepositoryId` | `Availability = 'Available' AND DeletedAtUtc IS NULL` | FR-034 race backstop (research D9). Case-insensitive through the column collation. |
| `UX_CustomModels_Destination_InProgress` on `Destination` | `IsInProgress = 1` | FR-015: one active job per destination, enforced in the database for equal paths (case-insensitive). Overlapping paths (`Models/a` against `Models/a/b`) are refused by the submit handler only (research D5). |
| `IX_CustomModels_RepositoryId_DeploymentState` | `DeletedAtUtc IS NULL` | The locator's one lookup per voice request (below). |
| `IX_CustomModels_CreatedAtUtc` | — | List ordering (research D13). |
| `IX_CustomModelOverwrittenFiles_CustomModelId` | — | Detail paging. |

### Locator query (research D9, FR-039)

`ICustomModelRepository.FindCompletedForRepositoryAsync(repositoryId)` returns the non-deleted
records with `DeploymentState = Completed` for the repository (case-insensitive). The locator maps
the result:

| Completed records | Resolution |
|---|---|
| none (including while a first deployment is Queued, running, Failed or Cancelled) | `NoRecord`: the engine keeps its configured folder |
| one of them is `Available` | `Available(Destination, Id)` |
| some, none `Available` | `Unavailable(reason)` |

A `DbUpdateException` caused by a unique-index violation is translated into
`DuplicateResourceException` (HTTP 409) with a message naming the conflicting field. This follows the
existing repository convention.

### State machine (Domain methods; an illegal transition throws `DomainRuleViolationException`)

```text
            ┌──────── RequestCancellation / MarkCancelled ─────────┐
            │                                                      ▼
 Create → Queued ─StartListing→ Listing ─BeginTransfer→ Transferring ─Complete→ Completed
            │                     │                        │
            └──────── Fail ───────┴────────── Fail ────────┴──→ Failed
                                  └── MarkCancelled ───────┴──→ Cancelled
```

| Method | Allowed from | Effect |
|---|---|---|
| `Create(name, source, destination, submittedBy)` | — | `Queued`, `Unavailable`, `IsInProgress = true`. |
| `StartListing(utcNow)` | `Queued` | `Listing`, sets `StartedAtUtc`. |
| `BeginTransfer(commitSha, totalBytes, fileCount)` | `Listing` | `Transferring`. Throws `SizeLimitExceeded` if `totalBytes > cap`; the cap is passed in, not read by the Domain. |
| `RecordOverwrite(path, previousSize, utcNow)` | `Transferring` | Appends an overwritten file and increments the count. |
| `Complete(utcNow)` | `Transferring`, only when `CompletedFileCount == TotalFileCount` | `Completed`, `IsInProgress = false`, clears the current-file fields. |
| `Fail(kind, reason, utcNow)` | `Queued`, `Listing`, `Transferring` | `Failed`, `IsInProgress = false`. |
| `RequestCancellation(userId, utcNow)` | `Queued`, `Listing`, `Transferring` | Sets `CancellationRequestedAtUtc`. From `Queued` it goes straight to `Cancelled`. |
| `MarkCancelled(utcNow)` | `Listing`, `Transferring` with a cancellation requested | `Cancelled`, `IsInProgress = false`. |
| `MakeAvailable()` | `DeploymentState == Completed` | `Available`. The rule that only one model per repository is Available is checked by the handler (it spans aggregates) and backstopped by the index. |
| `MakeUnavailable()` | any state | `Unavailable`. |
| `Remove(userId, utcNow)` | `Failed` or `Cancelled` | Soft delete, which frees the name (FR-031). |

Progress fields (`TransferredBytes`, `CompletedFileCount`, `CurrentFile*`) are written through
`ICustomModelRepository.UpdateProgressAsync`, which uses `ExecuteUpdateAsync` and only moves forward
(`WHERE TransferredBytes <= @new`). This is bookkeeping, not a state transition, so it bypasses the
aggregate's methods. See the memory note on Application never referencing EF Core.

Because that `UPDATE` still bumps the SQL Server `rowversion`, any aggregate save that can race it
(cancel, the recovery sweep, and the job's own transitions) reloads and retries once on a
concurrency conflict (research D6). Terminal transitions apply only while the record is still in
progress, so a record the sweep failed is never flipped back to `Completed`.

`GetRunSignalAsync(id)` is the job's per-flush read: `Continue`, `CancellationRequested`, or
`NoLongerInProgress` (research D7).

---

## Value object: `HuggingFaceModelSource` (Domain — `CustomModels/HuggingFaceModelSource.cs`)

`static bool TryParse(string raw, out HuggingFaceModelSource? source, out string? error)`

| Member | Example |
|---|---|
| `Owner` / `Repository` | `Supertone` / `supertonic-3` |
| `RepositoryId` | `Supertone/supertonic-3` |
| `Revision` | `main` (the default), `v1.0`, `refs/pr/3`, or a 40-hex sha |
| `IgnoredFilePath` | `onnx/vocoder.onnx` when a `/resolve/<rev>/<file>` URL was pasted (Edge Cases: the whole repository is still deployed, and the UI says so) |
| `DerivedName` | `supertonic-3`. Null only if the name wouldn't be a valid model name, so the Name field appears. |

The grammar and rejection rules are in research D4. Everything is a pure string operation with no
I/O, so all the path-traversal and host tests are Domain unit tests.

## Value object: `DeploymentDestination` (Domain — `CustomModels/DeploymentDestination.cs`)

- `static bool TryCreate(string raw, IReadOnlyList<string> allowedPrefixes, out DeploymentDestination?, out string? error)`
- `string Value` is the canonical relative path.
- `bool TryCombine(string repositoryRelativePath, out string? combined, out string? error)` applies
  the same segment rules to the file path, plus the `web.config`/`app_offline.htm` ban (FR-006).

The rules are in research D5. The FTP root is **not** a member. Joining with the root happens only
in the Infrastructure FTP adapter.

---

## Configuration (not persisted)

### `FtpOptions` — section `Ftp` (Infrastructure — `CustomModels/Deployment/FtpOptions.cs`)

| Key | Type | Default | Notes |
|---|---|---|---|
| `Host` | string | `""` | Blank means not configured (FR-019). |
| `Port` | int | `21` | |
| `Username` | string | `""` | |
| `Password` | string | `""` | Redacted from `ToString()`. Never logged or returned (research D1). |
| `RootPath` | string | `""` | For example `/hydra`. Must start with `/`. |
| `AllowPlainFtp` | bool | `false` | FR-018a. |

It is bound with `services.Configure<FtpOptions>(config.GetSection("Ftp"))`, **with no
`ValidateOnStart`**.

### `DeploymentTargetSettings` (Application — `CustomModels/Abstractions/DeploymentTargetSettings.cs`)

A connector-neutral record returned by `IDeploymentTargetSettingsProvider`, holding `Host`, `Port`,
`Username`, `Password`, `RootPath` and `AllowPlainFtp`. `PrintMembers` redacts the password. **This
record is the seam the future Connectors feature (spec 071) plugs into.**

### `CustomModelsOptions` — section `CustomModels` (Application — `Options/CustomModelsOptions.cs`)

| Key | Default | Notes |
|---|---|---|
| `MaxDeploymentBytes` | `21474836480` (20 GB) | FR-008. |
| `AllowedDestinationPrefixes` | `["Models", "App_Data/Models"]` | FR-005a (research D5). |
| `TempDirectory` | `App_Data/Temp/custom-models` | Resolved against the content root. |
| `ProgressPersistIntervalSeconds` | `2` | FR-021. |
| `ProgressPushIntervalMilliseconds` | `500` | SC-004. |
| `StallTimeoutSeconds` | `60` | research D6. |

All keys have defaults, so no `ValidateOnStart` risk arises.

---

## Voice DTO change (Application — `Ai/AdminVoiceProviderDto.cs`)

Two properties are added, both backward-compatible for existing clients:

| Field | Type | Values |
|---|---|---|
| `ModelStatus` | string | `Ready` or `ModelUnavailable` |
| `ModelStatusReason` | string? | For example "The Supertonic model is marked unavailable in Custom Models." |

`ElevenLabs` is always `Ready`. `Supertonic` depends on `IHostedModelLocator` (research D9).

## Migration

A new migration, `AddCustomModels`, creates both tables, the indexes, and the case-insensitive
collations on `Name`, `RepositoryId` and `Destination`. It is
additive only, so no backfill is needed: with no Completed rows, Supertonic keeps today's behaviour (FR-039).
The migration goes through the BOM and CRLF check described in the memory note on migration CI
gotchas.
