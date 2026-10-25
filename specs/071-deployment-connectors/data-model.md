# Data Model: Deployment Connectors (Admin)

**Spec**: [spec.md](spec.md) · **Research**: [research.md](research.md)

Two tables. `DeploymentConnectorTypes` is the admin-managed catalogue of connector types;
`DeploymentConnectors` holds the deployment targets. No existing table changes shape.

---

## Table: `DeploymentConnectorTypes`

The catalogue of connector types. Seeded with `Ftp` and `Ftps`; an administrator can add
more (each naming a protocol the server implements) and remove unused ones.

| Column | Type | Rules |
|---|---|---|
| `Id` | uniqueidentifier | PK. `Guid.CreateVersion7()`. |
| `Name` | nvarchar(100) | Required, trimmed. **Unique platform-wide** among non-deleted rows (FR-002, research D9). Collation `SQL_Latin1_General_CP1_CI_AS`, so the comparison ignores case. |
| `ProtocolKey` | nvarchar(50) | Required, trimmed. Selects the registered `IConnectorProtocol` implementation that defines this type's schema (research D1). Seeded `Ftp` → `ftp`, `Ftps` → `ftps` — two protocols over one FTP implementation, so `ftps` requires explicit TLS while `ftp` leaves it to the connector's `useExplicitTls` setting. |
| `CreatedAtUtc` / `CreatedBy` | datetime2 / nvarchar(450) | From `BaseEntity`, filled by `AuditSaveChangesInterceptor`. |
| `ModifiedAtUtc` / `ModifiedBy` | datetime2? / nvarchar(450)? | From `BaseEntity`. |
| `DeletedAtUtc` / `DeletedBy` | datetime2? / nvarchar(450)? | Soft delete (research D13). A global query filter hides deleted rows. |
| `RowVersion` | rowversion | Optimistic concurrency token. |

### Invariants (Domain — `DeploymentConnectorType`)

- `Create` rejects a blank name, a name over 100 characters, a blank protocol key, and a
  protocol key over 50 characters — `DomainRuleViolationException`.
- `Rename` (not needed today, kept out) — no rename operation exists; a type's name is
  fixed for its lifetime so a connector's type reference never changes meaning.
- Soft delete is refused by the Application layer while a non-deleted connector references
  the type (`DeploymentConnectorTypeInUseException` → 422).

### Indexes

| Index | Filter | Purpose |
|---|---|---|
| `IX_DeploymentConnectorTypes_Name` | `[DeletedAtUtc] IS NULL` | Unique platform-wide name (research D9). Same pattern as `AiCapabilitySetting` and `ModelDeprecation`. |
| `IX_DeploymentConnectorTypes_ProtocolKey` | — | Not indexed: the table is small and the column is only read for a whole-type query. |

---

## Table: `DeploymentConnectors`

The deployment targets other admin features push files to. One row per target.

| Column | Type | Rules |
|---|---|---|
| `Id` | uniqueidentifier | PK. `Guid.CreateVersion7()`. |
| `Name` | nvarchar(200) | Required, trimmed. **Unique platform-wide** among non-deleted rows — one shared list across all administrators, like Voice and AI providers (clarification, research D9). |
| `ConnectorTypeId` | uniqueidentifier | Required. FK → `DeploymentConnectorTypes`. Indexed. A connector's schema is its type's schema (research D1). |
| `Host` | nvarchar(400) | Required, trimmed. The host or endpoint. Never returned in a log or an exception message. |
| `Port` | int | Required. 1–65535. Defaults to the protocol's default port (21 for FTP). |
| `Username` | nvarchar(200) | Required, trimmed. The account used to authenticate. |
| `CredentialCiphertext` | nvarchar(4000) | Required. Data Protection ciphertext, purpose `AskLucy.DeploymentConnectorCredentials` (research D4). **Never serialized into a DTO.** |
| `CredentialHint` | nvarchar(20) | Nullable. `CredentialHintFormatter` output — first 4 + `...` + last 4, or `****` when the credential is shorter than 8 characters. |
| `CredentialLastRotatedAtUtc` | datetime2 | Nullable. Set when the credential is written. |
| `RemoteBasePath` | nvarchar(400) | Required, trimmed. The remote directory the connector writes into, for example `/hydra`. Must start with `/`. |
| `SettingsJson` | nvarchar(2000) | Nullable. The protocol's extra field values, for example `{"useExplicitTls":true,"usePassiveMode":false}` (research D3). `{}` when the protocol declares no extras. |
| `LastTestedAtUtc` | datetime2 | Nullable. When the connection was last verified successfully. |
| `LastTestFailureKind` | nvarchar(40) | Nullable. `ConnectorTestFailureKind` of the last failed test, cleared on a successful one. Nullable so the enum is a string in the database. |
| `CreatedAtUtc` / `CreatedBy` | datetime2 / nvarchar(450) | From `BaseEntity`. |
| `ModifiedAtUtc` / `ModifiedBy` | datetime2? / nvarchar(450)? | From `BaseEntity`. |
| `DeletedAtUtc` / `DeletedBy` | datetime2? / nvarchar(450)? | Soft delete. A global query filter hides deleted rows. |
| `RowVersion` | rowversion | Optimistic concurrency token (FR-017, research D8). |

### Invariants (Domain — `DeploymentConnector`)

- `Create` rejects: a blank or over-long name; a blank `ConnectorTypeId`; a blank host; a
  port outside 1–65535; a blank username; a blank remote base path; a remote base path that
  does not start with `/`; a remote base path over 400 characters; a blank credential
  ciphertext. All `DomainRuleViolationException`.
- `SetCredential(ciphertext, hint, actor)` replaces the ciphertext, the hint and
  `CredentialLastRotatedAtUtc`. It rejects a blank ciphertext. The plaintext never reaches
  the Domain.
- `UpdateDetails(...)` changes the name, host, port, username, remote base path and
  settings. It rejects the same invalid values as `Create`.
- `ChangeType(connectorTypeId, actor)` repoints the connector at another type.
- `RecordTestSuccess(atUtc)` sets `LastTestedAtUtc` and clears `LastTestFailureKind`.
- `RecordTestFailure(atUtc, kind)` sets `LastTestedAtUtc` and the failure kind.
- `SoftDelete(actor)` marks the row deleted; the interceptor keeps the row.

### Indexes

| Index | Filter | Purpose |
|---|---|---|
| `IX_DeploymentConnectors_Name` | `[DeletedAtUtc] IS NULL` | Unique platform-wide name (research D9). |
| `IX_DeploymentConnectors_ConnectorTypeId` | — | The "is this type in use?" check (FR-005, research D13). |
| `IX_DeploymentConnectors_DeletedAtUtc` | — | Serves the global query filter. |

---

## Value objects and supporting types

### `ConnectorTestFailureKind` (Domain)

Why the connection failed. This is what SC-005 asks the administrator to be able to tell
apart: reaching the host, authenticating, or writing to the remote path. It lives in the
Domain because the connector row records the last failure kind.

| Value | Meaning |
|---|---|
| `Unreachable` | The host could not be contacted. |
| `AuthRejected` | The server refused the login. |
| `TlsNotAccepted` | The server does not accept an encrypted connection. |
| `CertificateInvalid` | The server's TLS certificate is not trusted. |
| `PathMissing` | The remote base path does not exist. |
| `PathNotWritable` | The remote base path exists but cannot be written to. |

### `IConnectorProtocol` (Application — `Abstractions`)

The per-transport interface (research D1). One implementation per transport.

| Member | Type | Meaning |
|---|---|---|
| `ProtocolKey` | string | The stable key the type row references. |
| `DisplayName` | string | The human-readable transport name. |
| `DefaultPort` | int | The port used when the administrator does not choose one. |
| `Fields` | `IReadOnlyList<ConnectorFieldDescriptor>` | The protocol's extra fields (research D2). |
| `TestAsync(spec, cancellationToken)` | `Task<ConnectorTestResult>` | Connects, authenticates and proves write access (research D7). |

### `ConnectorFieldDescriptor` (Application — `Abstractions`)

Describes one protocol-specific field so the browser can render it without knowing the
protocol (research D2).

| Member | Type | Meaning |
|---|---|---|
| `Key` | string | The key in `SettingsJson`. |
| `Label` | string | The label shown in the form. |
| `Kind` | enum `Text` / `Number` / `Boolean` | The input to render. |
| `DefaultValue` | string? | The value used when the administrator leaves the field empty. |
| `Required` | bool | Whether the field must have a value. |

### `ConnectorTestResult` (Application — `Abstractions`)

| Member | Type | Meaning |
|---|---|---|
| `Succeeded` | bool | Whether the target is usable. |
| `FailureKind` | `ConnectorTestFailureKind?` | Why not, when it did not. |
| `Message` | string? | A safe, administrator-facing explanation. Never contains the host, the username or the credential. |

### `DeploymentConnectorSettings` (Domain)

A typed view over `SettingsJson`, so the Domain does not hand out raw JSON. It exposes
`GetBoolean(key)`, `GetString(key)` and `ToJson()`, and is built from the protocol's
descriptors.

---

## Migration

One additive migration, `AddDeploymentConnectors`:

1. Creates `DeploymentConnectorTypes` and `DeploymentConnectors` with the columns and
   indexes above.
2. Seeds two type rows: `Ftp` (protocol `ftp`) and `Ftps` (protocol `ftps`).

No existing table is altered, so the migration is reversible by dropping the two new tables
(FR-018 is not claimed for this feature; the `Down` is a drop).
