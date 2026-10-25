# Contract: Admin Deployment Connectors API

The base route is `api/v1/admin/connectors`, and every endpoint uses the `admin-endpoints`
rate limit. **view** means the `admin.connectors.view` permission and **manage** means
`admin.connectors.manage` (research D10). Neither is implied by `admin.ai-providers.*` or by
`admin.custom-models.*`.

Errors are returned as Problem Details:

| Condition | Status |
|---|---|
| Validation failed (name, host, port, base path, settings) | 400. `errors` is keyed by the field |
| Unknown connector or type id | 404 |
| A connector with that name already exists | 409. `detail` names the field |
| The row changed since it was loaded | 409, with a `currentConnector` extension (FR-017) |
| The type is still used by a connector | 422, with a `referencingConnectorCount` extension (FR-005) |
| The connection test failed | 422, with a `connectorFailure` extension (FR-014) |

| Method | Route | Permission | Body | Returns |
|---|---|---|---|---|
| GET | `` | view | query `page` (1), `pageSize` (50, max 100) | `PagedResult<DeploymentConnector>`, newest first |
| GET | `{id}` | view | — | `DeploymentConnector` |
| POST | `` | manage | `{ name, connectorTypeId, host, port, username, credential, remoteBasePath, settings }` | **201** `DeploymentConnector`. The connection is tested first; a failure is a 422 and nothing is persisted (FR-009) |
| PUT | `{id}` | manage | `{ name, host, port, username, remoteBasePath, settings, rowVersion }` | `DeploymentConnector`. **No credential field** — a credential is written only through the credential endpoint (FR-011) |
| PUT | `{id}/credential` | manage | `{ credential }` | `DeploymentConnector` with the new hint. The new credential is tested before it is stored |
| DELETE | `{id}` | manage | — | 204. The row is soft-deleted and its name is released |
| POST | `actions/test` | manage | `{ connectorTypeId, host, port, username, credential, remoteBasePath, settings }` | `ConnectorTestResult`. Tests the given values; **persists nothing** (FR-008) |
| GET | `types` | view | — | `DeploymentConnectorType[]` with each type's protocol fields |
| POST | `types` | manage | `{ name, protocolKey }` | **201** `DeploymentConnectorType` |
| DELETE | `types/{id}` | manage | — | 204. 422 while a connector still uses the type (FR-005) |

## Shapes

```ts
type ConnectorTestFailureKind =
  | 'Unreachable' | 'AuthRejected' | 'TlsNotAccepted'
  | 'CertificateInvalid' | 'PathMissing' | 'PathNotWritable'

type ConnectorFieldKind = 'Text' | 'Number' | 'Boolean'

interface ConnectorFieldDescriptor {
  key: string
  label: string
  kind: ConnectorFieldKind
  defaultValue: string | null
  required: boolean
}

interface DeploymentConnectorType {
  id: string
  name: string
  protocolKey: string
  protocolDisplayName: string
  defaultPort: number
  /** The protocol's extra fields; the form renders these in addition to the universal ones. */
  fields: ConnectorFieldDescriptor[]
  /** How many non-deleted connectors use this type. */
  connectorCount: number
}

interface DeploymentConnector {
  id: string
  name: string
  connectorTypeId: string
  connectorTypeName: string
  host: string
  port: number
  username: string
  remoteBasePath: string
  /** The protocol's extra field values. */
  settings: Record<string, string | number | boolean>
  /** True when a credential is stored. The value itself is never returned. */
  hasCredential: boolean
  /** First 4 + '...' + last 4 of the credential, or null. Never the credential. */
  credentialHint: string | null
  credentialLastRotatedAtUtc: string | null
  lastTestedAtUtc: string | null
  lastTestFailureKind: ConnectorTestFailureKind | null
  /** Base64 row version; sent back on update so a stale save is refused. */
  rowVersion: string
}

interface ConnectorTestResult {
  succeeded: boolean
  failureKind: ConnectorTestFailureKind | null
  /** A safe, administrator-facing explanation. Never contains the host, username or credential. */
  message: string | null
}

interface PagedResult<T> { items: T[]; totalCount: number; page: number; pageSize: number }
```

## Never returned

The credential is accepted on create and on the credential endpoint, and is returned by
neither. The DTO carries `hasCredential`, `credentialHint` and
`credentialLastRotatedAtUtc` and nothing else about it (FR-007). The host, the username and
the remote base path appear only in the connector DTO, which is what an administrator needs
to recognise a target — and they never appear in a log line or an exception message.

## For a future feature that pushes files to a connector

This feature is a building block; it does not open a session to a target on anyone's behalf.
A future feature (the model downloader) reads these fields of a connector row and opens an
FTP session with them:

| What it needs | Where it comes from |
|---|---|
| `Host`, `Port` | The connector's `Host` and `Port` columns, or the protocol's `DefaultPort`. |
| `Username` | The connector's `Username` column. |
| The credential | The connector's `CredentialCiphertext`, decrypted server-side with `IDeploymentConnectorCredentialProtector` (purpose `AskLucy.DeploymentConnectorCredentials`). It is never decrypted to the client. |
| `RemoteBasePath` | The connector's `RemoteBasePath` column — the directory the files go into. |
| The protocol | The connector's type's `ProtocolKey`. `ftp` and `ftps` speak FTP over explicit TLS; `sftp`, `s3` and `azureblob` will be added later (research D1). |
| The protocol's extra options | The connector's `SettingsJson` — for example `useExplicitTls` and `usePassiveMode` for FTP. |

The seam for that future work already exists: `IDeploymentTargetSettingsProvider` is the
place a connector-backed deployment target is plugged in (specs/072 research D1). A
connector-backed implementation of it returns the values above as
`DeploymentTargetSettings`; nothing in the current custom-model deployment path changes in
this feature (research D14).
