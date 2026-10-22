# Research: Deployment Connectors (Admin)

**Spec**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Date**: 2026-10-22

Each decision lists what was chosen, why, and the alternatives that were rejected. The
clarifications of 2026-09-23 are quoted where they drove the choice.

---

## D1 — The connector type is an admin-managed table row that names a code-registered protocol

**Decision.** Two tables. `DeploymentConnectorTypes` holds admin-managed rows
(`Name` unique platform-wide, `ProtocolKey`); `DeploymentConnectors` holds the deployment
targets, each pointing at a type row.

The protocol — not the type row — is the code-side extension point. An
`IConnectorProtocol` implementation is registered per protocol under a stable key
(`ftp`, `ftps` today; `sftp`, `s3`, `azureblob` later). The type row's `ProtocolKey`
selects the implementation, and the implementation declares **the schema of the connector**
— which fields exist and how they are validated.

`ftp` and `ftps` are two protocols, not two type aliases: `ftp` lets the administrator
choose per connector whether to use explicit TLS, `ftps` requires it. They share one
implementation class (`FtpConnectorProtocol`, registered under both keys with
`RequireExplicitTls` set accordingly) — the same class, two registrations, no duplicated
FTP logic. A later `sftp`, `s3` or `azureblob` is a new class.

**Why the two layers.** The request asks for a managed type table with a `+`/`−` control in the
create/edit form, seeded with `Ftp` and `Ftps`, and a table designed so `Sftp`/`S3`/`AzureBlob`
can be added later without breaking existing rows. The clarification adds that the type
"should define the name and schema of the connector, I think it will need separate interface".
Both are satisfied by: a **type** = a named, admin-curated profile; a **protocol** = the
interface that defines the schema. The `+` dialog offers the registered protocols, so an
admin can add `Staging FTPS` (protocol `ftps`) without a code change, and a future
`S3` protocol needs only a new implementation plus a type row.

**Rejected.**
- *One table, protocol as an enum column on the connector.* Adding `S3` would then be a
  migration that widens the enum, and the type would be a code constant rather than
  something an admin manages — the opposite of what was asked for.
- *Type row ⇄ protocol 1:1 (no protocol column).* Then an admin can only ever have one
  type per transport, and `Ftp` vs `Ftps` would have to be two protocol keys with identical
  behaviour. The separate `ProtocolKey` column lets several named types share a transport.

## D2 — The protocol declares its fields; the API returns them and the form renders them

**Decision.** `IConnectorProtocol` exposes `ProtocolKey`, `DisplayName`, `DefaultPort` and
`Fields`: a list of descriptors (`Key`, `Label`, `Kind`, `DefaultValue`, `Required`).
`GET /admin/connectors/types` returns each type row with its protocol's field descriptors.
The create/edit dialog renders the universal fields explicitly and the protocol's extra
fields from the descriptors.

**Why.** "The connector type should define the name and schema of the connector" — the
schema has to reach the browser, or the form cannot be built for a protocol the server did
not ship a bespoke form for. Descriptors are the smallest mechanism that carries it.

**Rejected.**
- *A `SettingsJson` blob with no schema.* The admin would be typing free-form keys with no
  validation and no labels — the UI would degrade into a JSON editor.
- *Per-protocol branches in the React form.* A new protocol would then need a frontend
  change, which defeats the point of the table being extensible.

## D3 — Universal fields are columns; protocol extras are a JSON settings column

**Decision.** `DeploymentConnectors` carries the fields every file-transfer target needs as
real columns: `Host`, `Port`, `Username`, `RemoteBasePath`, and the encrypted credential.
Everything else the protocol declares is stored in one `SettingsJson` column
(`{"useExplicitTls":true,"usePassiveMode":false}` today).

**Why.** The universal five map cleanly onto FTP, SFTP, S3 and Azure Blob alike
(endpoint, port, access-key id, secret, bucket/container prefix). The extras genuinely
differ per protocol, and one JSON column means a new protocol adds **no migration** — the
strongest form of "added later without breaking existing rows".

**Rejected.**
- *A column per protocol extra.* `S3` would need `Region` and `EndpointStyle` columns, and
  `Sftp` a `KeyPath` column: a migration each time, and a wide table where most columns
  are NULL for any given row.
- *Everything in JSON.* The universal fields are the ones the API, the audit log and the
  future downloader read by name; they belong in columns.

## D4 — The credential is protected with its own Data Protection purpose, and never leaves the server

**Decision.** A new `IDeploymentConnectorCredentialProtector` (Application) with a
`DeploymentConnectorCredentialProtector` (Infrastructure) using the purpose string
`AskLucy.DeploymentConnectorCredentials`, mirroring `IMcpCredentialProtector`. The API
exposes `hasCredential`, `credentialHint` and `credentialLastRotatedAtUtc` — never the
plaintext and never the ciphertext. The hint uses the house `CredentialHintFormatter`
(first 4 + `...` + last 4).

**Why.** "encrypted at rest using the same approach as VoiceProvider's credential" is the
mechanism — Data Protection with a hint and no value in any response. `McpCredentialProtector`
set the precedent for a **separate purpose string per credential class**, so a key-ring
compromise of one class does not unlock another; an FTP deployment password should not share
a purpose with AI provider keys.

**Rejected.**
- *Reuse `IAiCredentialProtector`.* Fewer lines, but it puts deployment passwords under the
  `AskLucy.AiProviderCredentials` purpose, which is exactly the coupling the MCP class
  documents as worth avoiding.

## D5 — The initial credential travels with the create request; every later credential write is its own endpoint

**Decision.** `POST /admin/connectors` accepts the credential (it is needed to test the
connection before the row can be saved). `PUT /admin/connectors/{id}` never accepts one.
`PUT /admin/connectors/{id}/credential` is the only other way to set or rotate it.

**Why.** "Credential writes are a separate PUT (never accept/return it on the general
update)". Creation is not an update: a connector cannot be created without a credential
because the create path verifies the connection first.

## D6 — Test-before-save is enforced on the server, not asserted by the client

**Decision.** `AddDeploymentConnector` always performs the connection test and refuses to
persist a connector that fails it. `UpdateDeploymentConnector` re-tests only when a
connection-relevant field changed (host, port, username, credential, base path, type or
settings) — a rename does not need a round trip to the remote host.

`POST /admin/connectors/actions/test` runs the same test against **unsaved** form values
and persists nothing, so the administrator can see the reason before pressing Save.

**Why.** FR-009 and SC-002 are about what the system guarantees, not what the browser
promises. A client-supplied "I tested it" flag can be skipped, replayed or raced; a
server-side test at write time cannot. The standalone action exists because the requirement
asks for a "Test Connection" action in the UI — it is the same code path with a different
sink.

**Rejected.**
- *A `testedAt` stamp written by the client.* A client can forge it, and a stale stamp
  says nothing about the current values.
- *A signed "test receipt" token.* More machinery (a signing key, a token format, an
  expiry) for a guarantee the server can simply re-derive at write time.

## D7 — The write probe creates nothing and leaves nothing behind

**Decision.** The test connects (explicit TLS where the protocol demands it, never `Auto`),
logs in, checks that the remote base path exists, writes one small marker file into it, and
deletes the marker. It never creates a remote directory.

**Why.** "verifies write access to the remote base path" and "logs in and verifies write
access ... before saving, without persisting a connector that fails the test". A test that
creates directories changes the target behind the administrator's back, and a test that
leaves a marker behind is litter on a production deployment root.

**Rejected.**
- *Directory listing instead of a write.* Listing proves read access, not write access, and
  the requirement is explicitly about writing.
- *Uploading a zero-byte file and leaving it.* Litter.

**Failure kinds** (`ConnectorTestFailureKind`): `AuthRejected`, `TlsNotAccepted`,
`CertificateInvalid`, `Unreachable`, `PathMissing`, `PathNotWritable`. These are what SC-005
asks for — the administrator can tell "reaching the host" from "authenticating" from
"writing to the remote path".

## D8 — Concurrent edits are refused with the current server-side values

**Decision.** The connector carries a `RowVersion`. An update sends the row version it was
loaded with; a mismatch raises `DeploymentConnectorConflictException`, which the middleware
maps to `409` with a `currentConnector` extension carrying the saved row's safe fields
(name, type, host, port, username, base path) — never the credential.

**Why.** "The later save is blocked with a conflict message showing the connector's
current server-side values; the admin must review those values and reapply their change."
The extension makes the review possible in one round trip, and the credential is excluded
because the same response must never carry credential material.

## D9 — Names are unique platform-wide, and soft-deleted rows release their name

**Decision.** Both `DeploymentConnector.Name` and `DeploymentConnectorType.Name` are unique
across the platform (the clarification: "all admins share one connector list, like
Voice/AI Providers"), enforced by a filtered unique index over non-deleted rows.

**Why.** A single shared list needs one global namespace, or two administrators could
silently create two different "Production" targets and a later feature would not know
which one it was given. Rows are soft-deleted (`AuditSaveChangesInterceptor` converts hard
deletes), so the unique index is filtered on `DeletedAtUtc IS NULL` — the
`AiCapabilitySetting` / `ModelDeprecation` pattern — otherwise a deleted name could never
be reused.

## D10 — A dedicated `admin.connectors.view` / `admin.connectors.manage` area

**Decision.** A new `AdminArea.Connectors` with two catalogue keys, mirroring every other
area. The catalogue count moves from 27 to 29, and the same keys are mirrored by hand into
the client-side `adminPermissions.ts` and into the English and Arabic `admin.roles`
catalogues.

**Why.** FR-012: "following this platform's existing admin permission scheme (e.g., a
`admin.connectors.view` / `admin.connectors.manage` style split between read and write
access)". Connectors are deployment targets, not AI providers: reusing
`admin.ai-providers.*` would hand FTP deployment credentials to a role that was granted
access to model vendor keys.

**Rejected.**
- *Reuse `admin.ai-providers.view/manage`, as `VoiceProvider` does.* Voice providers are
  vendor API keys and genuinely share the AI-provider permission. A deployment connector
  holds a server credential for a file-transfer host; the blast radius is different.
- *Fold connectors into `admin.custom-models.*`.* That area is about Hugging Face model
  deployments, not about where files are pushed.

**Cost, stated plainly:** the new area touches the shared catalogue, its domain test, the
client-side mirror, and the English and Arabic role catalogues. That is the price of a
separate permission, and it is a one-time cost.

## D11 — The FTP client seam is shared with the custom-model deployer, not duplicated

**Decision.** `IFtpClientFacade` and `AsyncFtpClientFacade` move from
`AskLucy.Infrastructure/CustomModels/Deployment` to a shared
`AskLucy.Infrastructure/Ftp` namespace, gaining three operations the connector test needs
(upload bytes, delete a file, test a directory). The custom-model uploader keeps using them
unchanged; the connector test uses them for its probe.

**Why.** The TLS mode, the certificate policy and the timeouts in that class are
security-relevant and already correct (`EncryptionMode` is never `Auto`,
`ValidateAnyCertificate` is false, only a certificate with no policy errors is accepted).
A second FTP client would be a second copy of those decisions, and the two would drift.

**Rejected.**
- *A separate FTP client for connectors.* Duplicates the certificate policy and the
  "never `Auto`" rule — exactly the kind of divergence that turns into a plain-text
  credential on the wire.
- *Reuse `IDeploymentFileUploader` for the probe.* It uploads a local file and has no
  delete, so the probe would leave a file on the target.

## D12 — No host or network restriction on connector targets

**Decision.** The connector test reaches whatever host the administrator configured. There
is no private-IP, loopback or link-local block.

**Why.** The clarification chose this explicitly: the feature is gated by
`admin.connectors.manage`, administrators are trusted operators, and this matches how the
existing admin-configured `Ftp` section in `appsettings` already behaves — the deployment
target it points at is equally unfiltered. The alternative (blocking private ranges) was
offered and declined.

**Recorded risk.** This is a deliberate, accepted difference from the MCP server feature,
which *does* block private addresses (`McpEndpointValidator`, research D4 of specs/018). A
`admin.connectors.manage` holder can therefore make the server open a connection to an
internal address. The mitigation is the permission, the audit log, and the fact that the
action is an administrator's own deployment target. The risk is stated here rather than
hidden; it is a conscious decision, not an oversight.

## D13 — Types are removed only when unused, and the removal is audited

**Decision.** `DELETE /admin/connectors/types/{id}` soft-deletes the type. If any
non-deleted connector still references it, the request is refused with `422` and a
`referencingConnectorCount` extension.

**Why.** FR-005: removal is prevented while a type is in use. Leaving a dangling
reference would make the connector's schema unknowable, which is the whole point of the
type.

**Rejected.**
- *Cascade the deletion.* Silently deleting deployment targets because a label was
  removed.
- *Let the reference dangle.* The connector's fields could then no longer be interpreted.

## D14 — The model downloader stays out of this feature

**Decision.** This feature delivers the record, its administration and the connection test.
It does not replace `ConfigurationDeploymentTargetSettingsProvider`, does not add a
downloader, and does not change how custom models are deployed today.

**Why.** The request: "This connector record is a building block only in this feature — do
not build the model downloader here. Add a note in the API contract about what a future
feature will need to read from a connector (host/credential/base path) to open an FTP
session server-side." The swap point for that future work is
`IDeploymentTargetSettingsProvider` (specs/072 research D1 calls it "the Connectors swap
point"); the contract note records exactly which connector fields a future provider reads
and how the credential is decrypted.

---

## Summary of the decisions that shape the code

| # | Decision | Where it lands |
|---|---|---|
| D1 | Type row names a registered protocol | `DeploymentConnectorType`, `IConnectorProtocol` |
| D2 | The protocol declares the field schema | `ConnectorFieldDescriptor`, `GET .../types` |
| D3 | Universal columns + JSON protocol extras | `DeploymentConnectors.SettingsJson` |
| D4 | Own Data Protection purpose, hint only | `IDeploymentConnectorCredentialProtector` |
| D5 | Credential write is its own endpoint | `PUT .../{id}/credential` |
| D6 | The server tests before it saves | `Add`/`UpdateDeploymentConnector` |
| D7 | Probe writes and removes one marker | `FtpConnectorProtocol` |
| D8 | Row version conflict returns current values | `DeploymentConnectorConflictException` |
| D9 | Platform-wide unique names, filtered index | `HasFilter("[DeletedAtUtc] IS NULL")` |
| D10 | New `admin.connectors.*` area | `AdminPermissionCatalog`, `adminPermissions.ts` |
| D11 | Shared FTP seam | `Infrastructure/Ftp` |
| D12 | No network restriction (accepted) | `FtpConnectorProtocol` |
| D13 | Type removal blocked while in use | `DeploymentConnectorTypeInUseException` |
| D14 | No downloader in this feature | contract note only |
