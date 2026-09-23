# Contract: Custom Model Deployment Hub

**Endpoint**: `/hubs/custom-model-deployments` (SignalR, JSON protocol). It is authenticated the same
way as the other hubs: the access token is delivered as a cookie (see the memory note on the SignalR
frozen-token bug).

**Access (FR-026)**: `OnConnectedAsync` resolves the caller's effective permissions. Callers holding
`admin.custom-models.view` join the group `custom-model-viewers`. Anyone else is disconnected with
`HubException("Forbidden")`. Clients never call server methods: the hub is push-only.

## Server → client events

### `CustomModelDeploymentProgress`

Sent at most every 500 ms per job, and always at file boundaries (SC-004 requires at least every
2 s).

```ts
interface CustomModelDeploymentProgress {
  customModelId: string
  deploymentState: 'Listing' | 'Transferring'           // lets a client that missed the state event still render the row
  transferredBytes: number; totalBytes: number        // overall
  completedFileCount: number; totalFileCount: number
  currentFilePath: string | null                      // relative to the repository
  currentFileBytes: number | null; currentFileTotalBytes: number | null
  phase: 'Downloading' | 'Uploading' | 'Verifying'    // for the current file
  overwrote: { relativePath: string; previousSizeBytes: number } | null   // set on the event that finished an overwrite (FR-010a)
  sentAtUtc: string
}
```

Overall bytes count **uploaded** bytes. Download progress is shown per file only (`phase`), so the
overall bar never moves backwards.

### `CustomModelDeploymentStateChanged`

Sent on every state transition and on availability or remove changes. The payload is the full
`CustomModelSummary` from [admin-custom-models.md](admin-custom-models.md), with a `removed: true`
flag after a DELETE.

## Client behaviour (`useCustomModelDeploymentsHub`)

- The hook connects only while `isAuthenticated` and the section is mounted, and uses
  `withAutomaticReconnect()`.
- It exposes `isLive`. While `isLive` is false, the section shows "Live updates disconnected —
  reconnecting…" (FR-024). A job's status is **never** shown as stalled because of a transport drop.
- On `onreconnected` it invalidates the `['admin','custom-models']` query, so REST fills any events
  it missed.
- Progress events patch the list query's cached row for that id. A state-changed event replaces the
  row.
- Events for an id not in the cache trigger a refetch.
- Connection errors surface through the `isLive` banner and are logged. No rejected promise from
  `start()` is left unhandled (constitution §2 VIII).
