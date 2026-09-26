# Contract: Admin Operational Failures API

The base route is `api/v1/admin/operational-failures`, and every endpoint uses the `admin-endpoints`
rate limit. The permissions are:

- **view** = `admin.operational-failures.view`
- **manage** = `admin.operational-failures.manage`, which implies view
- **content** = `admin.operational-failures.content.view`, which is meaningful only together with
  view

A missing permission returns 403. `PermissionDeniedAuditResultHandler` audits it and also records
it as an Access occurrence (research D13).

Errors are returned as Problem Details:

| Condition | Status |
|---|---|
| Invalid filter (unknown enum value, `from > to`, `pageSize > 100`, note > 500 characters) | 400. `errors` is keyed by field |
| Unknown incident, or an item that the incident does not reference (research D15) | 404 |
| Transition conflict: the incident is no longer in a state the transition applies to (someone else moved it first — research D19), or reopening while a newer incident for the same key is open | 409 with `type: …/incident-conflict`. `detail` says what changed. `newerIncidentId` is set when it applies. |

## Incidents

| Method | Route | Permission | Input | Returns |
|---|---|---|---|---|
| GET | `incidents` | view | query below | `PagedResult<IncidentSummary>` (`Items`, `TotalCount`, `Page`, `PageSize`), ordered by `lastSeenUtc` descending |
| GET | `incidents/{id}` | view | — | `IncidentDetail` |
| GET | `incidents/{id}/occurrences` | view | `page` (1), `pageSize` (50, max 100) | `PagedResult<Occurrence>`, newest first |
| GET | `incidents/{id}/related` | view | `page`, `pageSize` | `PagedResult<IncidentSummary>`: other **unresolved** incidents that share its `rootCauseKey`, most recently seen first (FR-026b) |
| POST | `incidents/{id}/actions/acknowledge` | manage | — | `IncidentDetail` |
| POST | `incidents/{id}/actions/resolve` | manage | `{ note? }` (body optional) | `IncidentDetail` |
| POST | `incidents/{id}/actions/reopen` | manage | — | `IncidentDetail` |
| POST | `root-causes/{rootCauseKey}/actions/acknowledge` | manage | — | `BulkTransitionResult` |
| POST | `root-causes/{rootCauseKey}/actions/resolve` | manage | `{ note? }` | `BulkTransitionResult` |
| GET | `summary` | view | — | `{ unacknowledgedCriticalRootCauses: number }`. This drives the badge (FR-026). |

**List query parameters.** All are optional. A parameter given more than once is OR-ed, and
different parameters are AND-ed.

| Param | Values | Default |
|---|---|---|
| `from`, `to` | ISO-8601 UTC. Filters on `lastSeenUtc`. | `now − 7 d` … now |
| `state` | `Open`, `Acknowledged`, `Resolved`, `Unresolved` | `Unresolved` (Open + Acknowledged) |
| `severity` | `Warning`, `Error`, `Critical` | all |
| `engine` | `OperationalFailureEngine` | all |
| `provider` | provider name | all |
| `kind` | `OperationalFailureKind` | all |
| `userId` | user id. Matched through participants. | — |
| `page`, `pageSize` | 1…, 1–100 | 1, 25 |

The time presets (last hour, 24 h, 7 d, 30 d, custom) are a client concern. They map to `from`/`to`.

## Investigations (read-only; research D15)

| Method | Route | Permission | Returns |
|---|---|---|---|
| GET | `incidents/{incidentId}/chats/{chatId}` | view (content adds the transcript) | `ChatInvestigation` |
| GET | `incidents/{incidentId}/workflow-runs/{executionId}` | view (content adds step I/O) | `WorkflowRunInvestigation` |
| GET | `incidents/{incidentId}/documents/{documentId}` | view (content adds extracted text) | `DocumentInvestigation` |

There are no POST, PUT or DELETE routes under investigations (FR-016b). When the caller holds
**content** and is not the owner, the server writes a `UserContentAccessEvent` **before**
returning. If that write fails, the request fails with 500 and returns no content (FR-016c).

## Shapes

```ts
type Severity = 'Warning' | 'Error' | 'Critical'
type Engine = 'Chat' | 'AiProvider' | 'Voice' | 'Embeddings' | 'DocumentProcessing' | 'ImageGeneration'
            | 'Agent' | 'Workflow' | 'Mcp' | 'BackgroundJob' | 'Access'
type TriageState = 'Open' | 'Acknowledged' | 'Resolved'

interface UserRef { id: string | null; displayName: string | null; email: string | null
                    status: 'Active' | 'Deleted' | 'Erased' }           // FR-016, FR-016d, FR-029a

interface IncidentSummary {
  id: string; rowVersion: string                                        // base64
  severity: Severity; engine: Engine; operation: string; kind: string
  providerId: string | null; providerName: string | null; model: string | null
  subject: { type: 'Workflow' | 'Agent' | 'Document' | 'McpServer'; id: string; label: string | null; deleted: boolean } | null
  firstSeenUtc: string; lastSeenUtc: string
  occurrenceCount: number; storedOccurrenceCount: number               // FR-022
  distinctUserCount: number; distinctSourceCount: number; recoveryCount: number
  latestReason: string; latestCorrelationId: string
  state: TriageState
  rootCauseKey: string; relatedOpenCount: number                        // "N others share this cause"
  isRecurrence: boolean
}

interface IncidentDetail extends IncidentSummary {
  recurrenceOfIncidentId: string | null
  acknowledged: { by: UserRef; atUtc: string } | null
  resolved: { by: UserRef; atUtc: string; note: string | null } | null
  correctiveAction: { text: string; adminRoute: string | null; adminAction: 'OpenJobsDashboard' | null }  // FR-014
  providerHealth: { status: string; failureKind: string | null; checkedAtUtc: string | null } | null   // FR-017
  sampleUsers: UserRef[]                                                // up to 10 most recent distinct users
  canManage: boolean; canViewContent: boolean
}

interface Occurrence {
  id: string; occurredAtUtc: string; severity: Severity; kind: string
  reason: string; correlationId: string; isFailover: boolean
  user: UserRef | null
  chat:        { id: string; title: string | null; deleted: boolean } | null
  messageId: string | null
  workflow:    { id: string; name: string | null; executionId: string | null; nodeId: string | null; deleted: boolean } | null
  document:    { id: string; name: string | null; knowledgeBaseId: string | null; deleted: boolean } | null
  agent:       { id: string; name: string | null; executionId: string | null; deleted: boolean } | null
  mcpServer:   { id: string; name: string | null; deleted: boolean } | null
  jobId: string | null
  sourceIp: string | null                                               // Access engine only
}

interface BulkTransitionResult { attempted: number; succeeded: number; skipped: number
                                 failed: { incidentId: string; reason: string }[] }   // FR-026b

interface ChatInvestigation {
  chat: { id: string; title: string; owner: UserRef; createdAtUtc: string; lastActivityUtc: string
          messageCount: number; deleted: boolean }
  failurePoints: { occurrenceId: string; turnNumber: number | null; occurredAtUtc: string; messageId: string | null }[]
  transcript: { id: string; role: 'user' | 'assistant' | 'system'; createdAtUtc: string; content: string
                isFailedTurn: boolean }[] | null                        // null unless content permission
}

interface WorkflowRunInvestigation {
  workflow: { id: string; name: string; owner: UserRef; deleted: boolean }
  run: { id: string; status: string; startedAtUtc: string; finishedAtUtc: string | null; stepCount: number }
  failedStep: { nodeId: string; name: string; type: string } | null
  steps: { nodeId: string; name: string; status: string; input: unknown; output: unknown; isFailedStep: boolean }[] | null
}

interface DocumentInvestigation {
  document: { id: string; name: string; contentType: string; sizeBytes: number; knowledgeBase: { id: string; name: string } | null
              processingStatus: string; owner: UserRef; deleted: boolean }
  extractedText: string | null                                          // null unless content permission; truncated to 200 KB with a flag
  extractedTextTruncated: boolean
}
```

**Server-side guarantees tested in Web.Tests:**
- No response to a caller without **content** contains `transcript`, `steps` or `extractedText`
  with a non-null value (SC-009).
- `reason` never exceeds 500 characters or contains a line break.
- `sourceIp` is null for every non-Access occurrence.
