# Contract: Admin Custom Models API

The base route is `api/v1/admin/custom-models`, and every endpoint uses the `admin-endpoints` rate
limit. **view** means the `admin.custom-models.view` permission and **manage** means
`admin.custom-models.manage`. Neither is implied by `admin.ai-providers.*`. A missing permission
returns 403, which `PermissionDeniedAuditResultHandler` audits.

Errors are returned as Problem Details:

| Condition | Status |
|---|---|
| Validation failed (source, destination, or name) | 400. `errors` is keyed by `source`, `destination` or `name` |
| Deployment not configured | 400 with `type: …/deployment-not-configured` |
| Rule violation (for example making a model Available before its deployment completed, or removing a completed model) | 400 |
| Unknown id | 404 |
| Duplicate name, a destination equal to or overlapping an active job's destination (FR-015), or another model already Available for the repository | 409. `detail` names the conflicting model |

| Method | Route | Permission | Body | Returns |
|---|---|---|---|---|
| GET | `` | view | query `page` (1), `pageSize` (50, max 100) | `Paged<CustomModelSummary>`, newest first |
| GET | `{id}` | view | query `overwrittenPage` (1), `overwrittenPageSize` (100, max 500) | `CustomModelDetail` |
| GET | `deployment-status` | view | — | `DeploymentStatus` |
| POST | `source-preview` | manage | `{ source }` | `SourcePreview`. Parses the URL only, **no outbound call** |
| POST | `` | manage | `{ source, destination, name? }` | **202** `CustomModelSummary` in state `Queued`, with a `Location: {id}` header |
| POST | `{id}/actions/cancel` | manage | — | 202 `CustomModelSummary`. 409 if already in a terminal state. Follows the constitution §6 `{id}/actions/{verb}` convention |
| PUT | `{id}/availability` | manage | `{ availability: 'Available' \| 'Unavailable' }` | `CustomModelSummary` |
| DELETE | `{id}` | manage | — | 204. Only `Failed` or `Cancelled`; 400 otherwise. The record is soft-deleted and **no remote files are touched** |

## Shapes

```ts
type DeploymentState = 'Queued' | 'Listing' | 'Transferring' | 'Completed' | 'Failed' | 'Cancelled'
type Availability = 'Available' | 'Unavailable'

interface CustomModelSummary {
  id: string; name: string
  repositoryId: string; revision: string; resolvedCommitSha: string | null; sourceUrl: string
  destination: string                 // relative to the root only; never includes RootPath
  deploymentState: DeploymentState; availability: Availability
  canMakeAvailable: boolean; availabilityBlockedReason: string | null   // "deployment has not completed"
  canRemove: boolean; canCancel: boolean
  totalBytes: number | null; transferredBytes: number
  totalFileCount: number | null; completedFileCount: number
  currentFilePath: string | null; currentFileBytes: number | null; currentFileTotalBytes: number | null
  overwrittenFileCount: number
  failureKind: string | null; failureReason: string | null
  submittedBy: { id: string; displayName: string }
  createdAtUtc: string; startedAtUtc: string | null; finishedAtUtc: string | null
  backsEngine: string | null           // e.g. "Supertonic" when repositoryId matches a hosted engine; not shown in the UI since 2026-09-24
}

interface CustomModelDetail extends CustomModelSummary {
  overwrittenFiles: Paged<{ relativePath: string; previousSizeBytes: number; overwrittenAtUtc: string }>
}

interface DeploymentStatus {
  isConfigured: boolean                // never exposes host, username, password or root path
  transport: 'FTPS' | 'FTP' | null     // 'FTP' only when AllowPlainFtp is set, shown as a warning
  maxDeploymentBytes: number
  allowedDestinationPrefixes: string[]
}

interface SourcePreview {
  isValid: boolean; error: string | null
  repositoryId: string | null; revision: string | null
  ignoredFilePath: string | null       // "Only whole repositories are deployed; onnx/x.onnx is ignored"
  derivedName: string | null
  nameAvailable: boolean               // false → the dialog shows the required Name field
}

interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number }
```

## Validation (FluentValidation plus Domain value objects)

- `source` is required, at most 2048 characters, and must satisfy
  `HuggingFaceModelSource.TryParse` ([data-model](../data-model.md)).
- `destination` is required, at most 512 characters, and must satisfy
  `DeploymentDestination.TryCreate` with the configured prefixes.
- `name` is optional, at most 100 characters, and must match `^[\p{L}\p{N}][\p{L}\p{N} ._-]{0,99}$`.
  It is required when the derived name is null or already taken (the handler checks, and returns a
  409 naming the model).
- The submit command re-checks `isConfigured`, so a race with a config edit still gets a clear 400.

## Guarantees

- No response, log line or exception message contains the FTP password. The host, username and
  root path are never returned either (FR-017, FR-018).
- `POST` returns once the record is saved and the job enqueued. It never waits for any network
  transfer (FR-012).
- Every manage action writes a `CustomModelAdminActionLog` security event (FR-027).

---

## Changed: Admin Voice API (`api/v1/admin/voice`, spec 070)

- `GET engines`: an `IHostedModelEngine` whose backing model resolves to `Unavailable` is **left
  out** (FR-036). If no **Completed** custom model record exists for its repository, the engine is
  listed as before (FR-039); Queued, running, Failed and Cancelled records don't count.
- `AdminVoiceProvider` gains
  `modelStatus: 'Ready' | 'ModelUnavailable'; modelStatusReason: string | null` (FR-037).
- `POST providers/{id}/preview` for a provider whose model is unavailable returns the existing
  provider-failure Problem Details, with the reason from `AiProviderUnavailableException`.
