# Implementation Plan: Deployment Connectors (Admin)

**Branch**: `071-deployment-connectors` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)
**Data model**: [data-model.md](data-model.md) · **Contract**: [contracts/admin-connectors.md](contracts/admin-connectors.md)
**Research**: [research.md](research.md) (D1–D14)

## Summary

Administrators get **Admin → Connectors**: a shared, platform-wide list of deployment
targets that other admin features can push files to. A connector names a host, a port, an
account, a credential and a remote base path, and belongs to a **connector type** — a named
profile that decides which fields apply and which protocol speaks to the target.

Two things make this more than a form:

- **The type is data, the protocol is code.** `DeploymentConnectorTypes` is a table an
  administrator manages from the form itself: a `+` adds a type, a `−` removes an unused one
  with a confirmation. Each type names a registered protocol (`ftp`, `ftps` today; `sftp`,
  `s3`, `azureblob` later), and the protocol declares the connector's schema. Adding a
  transport later is a new implementation and a new row — no migration, and no existing
  connector row changes meaning (research D1, D2).
- **The server verifies before it saves.** Creating a connector tests the connection first
  and refuses to persist a target that fails, so every saved connector is known to work
  against its current details. The "Test Connection" action runs the same test against
  unsaved form values so the administrator sees the reason — reaching the host,
  authenticating, or writing to the remote path — before pressing Save (research D6, D7).

The credential is encrypted with its own Data Protection purpose and never returned by the
API; only a hint and a rotation timestamp come back (research D4). This feature is a
building block: it does not replace the custom-model deployment target and does not add a
downloader. The contract records exactly what a future feature reads from a connector to
open an FTP session (research D14).

## Technical Context

**Language/Version**: C# / .NET 10 (backend); TypeScript 5 / React 19 (ClientApp).

**Primary Dependencies**:
- Existing: MediatR, FluentValidation, EF Core (SQL Server), ASP.NET Core SignalR, MUI,
  TanStack Query, `@microsoft/signalr`.
- **New**: none. `FluentFTP` 55.0.0 is already referenced by `AskLucy.Infrastructure` for
  the custom-model deployment and is reused for the connector test (research D11).

**Storage**:
- SQL Server: two new tables, `DeploymentConnectorTypes` and `DeploymentConnectors` (one
  additive migration).
- Remote: the deployment target itself.

**Testing**: xUnit, NSubstitute, FluentAssertions and `FakeLogger` (backend); Vitest, React
Testing Library and MSW (frontend); `CustomWebApplicationFactory` for Web.Tests.

**Target Platform**: IIS on the site4now shared host (production).

**Project Type**: Web application (ASP.NET Core API plus the React SPA in
`src/AskLucy.Web/ClientApp`).

**Performance Goals**:
- A connection test finishes within the FTP client's 60-second timeout; the request itself
  is a single round trip to the target.
- The connector list is an offset-paged query on two small tables.

**Constraints**:
- The credential never appears in a response, a log line or an exception message.
- The connection test creates no remote directory and leaves no file behind.
- No host or network restriction on the target (research D12 — a deliberate, accepted
  difference from the MCP feature).
- `appsettings` is not touched: a connector is a database row, not configuration.

**Scale/Scope**: a handful of deployment targets, managed by a few administrators. One
connector type per transport, plus any extra named types an administrator adds.

All unknowns are resolved in [research.md](research.md) (D1–D14). There are no remaining
NEEDS CLARIFICATION items.

## Constitution Check

*GATE: checked before Phase 0 and re-checked after Phase 1 design.*

| Principle | Status | How the design complies |
|---|---|---|
| **I. Clean Architecture (NON-NEGOTIABLE)** | ✅ | See the layer notes below. |
| **II. SOLID** | ✅ | `IConnectorProtocol` is narrow (describe + test). The FTP client seam stays a separate abstraction from the protocol that uses it (ISP). |
| **III. Simplicity / YAGNI** | ✅ | One new package: no AWS SDK, no SSH.NET, no `S3`/`Sftp` code. No downloader, no provider swap, no type editing. The JSON settings column is the only concession to future protocols and it replaces a migration per protocol. |
| **V. Dependency Inversion / Testability** | ✅ | The Application layer talks to `IConnectorProtocol` and `IDeploymentConnectorCredentialProtector`, both faked in tests. The FTP client sits behind the existing `IFtpClientFacade` seam, so a fake client drives the probe tests. |
| **VIII. No Silent Failures (NON-NEGOTIABLE)** | ✅ | A failed test is a 422 carrying a `connectorFailure` extension with the kind and a safe message; the frontend shows it. A failed save persists nothing. A 409 carries the current server-side values. Every admin action is logged. |
| **§3 Domain events** | ✅ | No cross-aggregate reaction is needed; the audit log and the SignalR-free admin screen are enough. |
| **§3 Infrastructure isolation** | ✅ | A new transport is a new `IConnectorProtocol` implementation plus a DI line (research D1). The FTP client is shared, not duplicated (research D11). |
| **§5 Database** | ✅ | Code-first additive migration. Surrogate `Guid` v7 keys, filtered unique indexes for the platform-wide names, soft delete with a global query filter, `RowVersion` for concurrency, enums stored as strings. |
| **§6 API** | ✅ | `/api/v1/admin/connectors`, Problem Details throughout, offset paging on a small admin list, `admin-endpoints` rate limiting, and the `{id}/actions/{verb}` shape for the non-CRUD test action. |
| **§8 Security** | ✅ | See the security notes below. |
| **§10 Testing** | ✅ | Domain, Application, Infrastructure and Web tests are all planned, including the FTP-failure and credential-leak cases; see the Testing strategy. |
| **§13 Documentation** | ✅ | This plan, the contract, the data model, plus `docs/DATABASE.md` and `docs/ARCHITECTURE.md`. |

**Principle I, layer by layer:**
- Domain holds `DeploymentConnector` and `DeploymentConnectorType`: pure aggregates with
  their invariants, plus the `DeploymentConnectorSettings` value object. No I/O, no JSON
  library, no EF attributes.
- Application holds the commands, queries, DTOs, `IConnectorProtocol`,
  `IDeploymentConnectorCredentialProtector` and the repository interfaces. It has no
  `HttpClient`, no EF Core and no FluentFTP.
- Infrastructure holds the FTP implementation of `IConnectorProtocol`, the credential
  protector, and the relocated FTP client seam.
- Persistence holds the EF configurations, the repositories and the migration.
- The controller calls only `ISender`.

**§8 Security, in detail:**
- The credential is encrypted with its own Data Protection purpose, so a key-ring
  compromise of one credential class does not unlock another (research D4). It is never
  returned by the API, and the DTO carries a hint only.
- The host, the username and the remote base path are administrator-facing values, so they
  are returned in the DTO — but they never appear in a log line or an exception message.
- Every write requires `admin.connectors.manage`; every read requires
  `admin.connectors.view`. Neither is implied by the AI-provider permissions (research
  D10).
- The connection test opens a connection to whatever host the administrator configured,
  with no private-address block (research D12 — an accepted, recorded risk).
- Every administrative action is logged through a `[LoggerMessage]` audit class, following
  `AiAdminActionLog` and `CustomModelAdminActionLog`.

**Post-design re-check (after Phase 1):** ✅ Passes. The design added the `SettingsJson`
column and the relocated FTP seam, both of which are additive and reversible.

## Project Structure

### Documentation (this feature)

```text
specs/071-deployment-connectors/
├── plan.md               # This file
├── research.md           # D1–D14 decisions
├── data-model.md         # The two tables, the value objects, the migration
├── quickstart.md         # Validation run-book
├── contracts/
│   └── admin-connectors.md  # REST contract, plus what a future consumer reads
├── checklists/requirements.md
└── tasks.md              # /speckit-tasks (not created here)
```

### Source Code

```text
src/AskLucy.Domain/
├── Authorization/AdminPermissionCatalog.cs      # + AdminArea.Connectors, connectors.view/manage
│                                                #   (the catalogue count 27 -> 29)
└── Connectors/
    ├── DeploymentConnector.cs                     # aggregate: identity, credential, settings, test state
    ├── DeploymentConnectorType.cs                # aggregate: the admin-managed type
    ├── DeploymentConnectorSettings.cs            # value object over the settings JSON
    └── ConnectorTestFailureKind.cs               # why a test failed

src/AskLucy.Application/
├── Connectors/
│   ├── Abstractions/
│   │   ├── IConnectorProtocol.cs                 # per-transport interface: describe + test
│   │   ├── ConnectorFieldDescriptor.cs           # one protocol field
│   │   ├── ConnectorTestResult.cs                # succeeded / kind / safe message
│   │   ├── IDeploymentConnectorCredentialProtector.cs  # its own Data Protection purpose
│   │   ├── IDeploymentConnectorRepository.cs
│   │   └── IDeploymentConnectorTypeRepository.cs
│   ├── Commands/
│   │   ├── AddDeploymentConnector/                # command, validator, handler (tests before saving)
│   │   ├── UpdateDeploymentConnector/            # re-tests only when connection details changed
│   │   ├── SetDeploymentConnectorCredential/      # the separate credential write
│   │   ├── DeleteDeploymentConnector/
│   │   ├── TestDeploymentConnector/               # tests unsaved values, persists nothing
│   │   ├── AddDeploymentConnectorType/
│   │   └── DeleteDeploymentConnectorType/         # refuses while a connector uses it
│   ├── Queries/
│   │   ├── ListDeploymentConnectors/
│   │   ├── GetDeploymentConnector/
│   │   └── ListDeploymentConnectorTypes/
│   ├── DeploymentConnectorDtos.cs
│   ├── DeploymentConnectorAdminActionLog.cs      # [LoggerMessage] audit events
│   ├── DeploymentConnectorConflictException.cs   # 409 + the current server-side values
│   ├── DeploymentConnectorTestFailedException.cs # 422 + the failure kind
│   └── DeploymentConnectorTypeInUseException.cs  # 422 + the referencing count

src/AskLucy.Infrastructure/
├── Ftp/                                              # MOVED from CustomModels/Deployment (research D11)
│   ├── IFtpClientFacade.cs                         # + upload bytes, delete file, directory exists
│   └── AsyncFtpClientFacade.cs
├── Connectors/
│   ├── FtpConnectorProtocol.cs                     # the ftp/ftps implementation, registered under both keys
│   └── DeploymentConnectorCredentialProtector.cs    # purpose AskLucy.DeploymentConnectorCredentials
└── DependencyInjection.cs                           # registers the protocols and the protector

src/AskLucy.Persistence/
├── AskLucyDbContext.cs                             # + DbSet<DeploymentConnector>, DbSet<DeploymentConnectorType>
├── Configurations/Connectors/
│   ├── DeploymentConnectorConfiguration.cs
│   └── DeploymentConnectorTypeConfiguration.cs
├── Repositories/DeploymentConnectorRepository.cs
├── Repositories/DeploymentConnectorTypeRepository.cs
└── Migrations/<ts>_AddDeploymentConnectors.cs      # creates both tables, seeds Ftp and Ftps

src/AskLucy.Web/
├── Controllers/v1/AdminDeploymentConnectorsController.cs
├── Contracts/DeploymentConnectorContracts.cs
├── Middleware/ProblemDetailsMiddleware.cs         # + 409 conflict, 422 in-use, 422 test failure
└── ClientApp/src/features/admin/
    ├── adminPermissions.ts                         # + connectors.view/manage
    ├── adminNav.tsx                                # + the Connectors entry
    ├── i18n/messages/{en,ar}/admin/roles.ts        # + the Connectors area and its two permissions
    ├── api/adminDeploymentConnectorsApi.ts
    ├── components/connectors/
    │   ├── DeploymentConnectorFormDialog.tsx       # the create/edit form
    │   ├── DeploymentConnectorTypeMenu.tsx         # the +/− control next to the type dropdown
    │   └── DeploymentConnectorSettingsFields.tsx   # renders the protocol's declared fields
    └── pages/AdminDeploymentConnectorsPage.tsx

docs/DATABASE.md                                    # + a section for the two new tables
docs/ARCHITECTURE.md                                # + a section for the type/protocol split
```

**Structure Decision**: A new `Connectors/` folder per layer, matching how `CustomModels/`,
`Mcp/` and `Workflows/` are laid out. The FTP client seam moves to a shared `Ftp/` folder
because two features now use it (research D11). The frontend lives under `features/admin`,
because the page is an admin tab.

**Documentation updates**: `docs/DATABASE.md` gains a section describing
`DeploymentConnectorTypes` and `DeploymentConnectors` with their columns and indexes, next to
the `CustomModels` and `VoiceProviders` sections. `docs/ARCHITECTURE.md` gains a section
explaining the type-table / protocol-implementation split, the server-side test-before-save
rule, and the note that the model downloader is a separate future feature that will read a
connector through `IDeploymentTargetSettingsProvider`.

## Key flows

1. **List** (`GET /admin/connectors`): the query returns the connectors with their type,
   their settings and their credential hint. The credential ciphertext is never selected.
2. **Create** (`POST /admin/connectors`):
   1. The validator checks the name, host, port, username, base path and settings against
      the type's protocol fields.
   2. The handler checks the name is free, then **tests the connection**: connect,
      authenticate, prove write access to the remote base path, remove the marker.
   3. A failure throws `DeploymentConnectorTestFailedException` → 422 with a
      `connectorFailure` extension, and **nothing is persisted** (FR-009).
   4. On success the credential is encrypted, the row is created, the audit event is
      written, and 201 is returned.
3. **Update** (`PUT /admin/connectors/{id}`): the row version is compared first. A mismatch
   throws `DeploymentConnectorConflictException` → 409 with the current server-side values
   (FR-017). The connection is re-tested **only** when a connection-relevant field changed;
   a rename is saved without a round trip to the target.
4. **Credential** (`PUT /admin/connectors/{id}/credential`): the new credential is tested
   against the saved host and base path first, then encrypted and stored. A wrong credential
   is refused and the old one stays.
5. **Delete** (`DELETE /admin/connectors/{id}`): the row is soft-deleted, its name is
   released for reuse, and the audit event is written.
6. **Type add** (`POST /admin/connectors/types`): the name must be free platform-wide; the
   protocol key must name a registered protocol, so an administrator cannot create a type
   the server could never connect.
7. **Type remove** (`DELETE /admin/connectors/types/{id}`): refused with 422 while a
   non-deleted connector still uses the type (FR-005).
8. **Test** (`POST /admin/connectors/actions/test`): the same probe as the create path,
   against the submitted values, persisting nothing. The result carries the failure kind
   and a safe message.

## Testing strategy

These cases are required.

- **Domain**:
  - `DeploymentConnector.Create` rejects a blank name, a blank host, a port outside
    1–65535, a blank username, a blank or relative remote base path, and a blank
    credential ciphertext.
  - `SetCredential` rejects a blank ciphertext and updates the hint and the rotation
    timestamp.
  - `UpdateDetails` rejects the same invalid values.
  - `RecordTestSuccess` clears the failure kind; `RecordTestFailure` sets it.
  - `DeploymentConnectorType.Create` rejects a blank name and a blank protocol key.
- **Application** (fakes for the protocol, the repository, the protector and the unit of work):
  - **A failing connection test on create persists nothing** and reports the failure kind
    (FR-009).
  - A failing connection test on update leaves the stored row untouched.
  - An update that changes only the name does **not** re-test.
  - An update that changes the host re-tests.
  - A duplicate name is refused before any test runs.
  - A stale row version is refused with the current server-side values.
  - A type that is still in use cannot be removed; one that is not can.
  - The DTO carries the hint and the rotation timestamp and **never** the ciphertext or the
    plaintext (FR-007).
  - The audit events are written for create, update, credential, delete and type changes.
- **Infrastructure** (a fake FTP client):
  - The probe writes a marker and **removes it**; the remote directory is not created.
  - **Auth rejected → `AuthRejected`; TLS refused → `TlsNotAccepted`; an untrusted
    certificate → `CertificateInvalid`; an unreachable host → `Unreachable`; a missing
    directory → `PathMissing`; a refused write → `PathNotWritable`.**
  - Explicit TLS is used where the type demands it and `Auto` is never used.
  - The credential protector round-trips a value and produces a different ciphertext each
    time.
- **Web**:
  - 401 without a token, 403 without the permission, 404 for an unknown id, 409 for a
    duplicate name, 409 for a stale row version, 422 for an in-use type, 422 for a failed
    test.
  - The response body of every endpoint contains no credential material.
- **Frontend**:
  - The list renders the connectors and their credential hint.
  - The form shows the type's declared fields and hides the ones the protocol does not
    declare.
  - The `+` adds a type and the `−` asks for confirmation before removing one.
  - A failed test shows the reason; a failed save shows the error and the connector is not
    added to the list.

## Risks

| Risk | Mitigation |
|---|---|
| The connection test reaches an internal address (no private-IP block) | Accepted and recorded (research D12). Gated by `admin.connectors.manage`, audited, and consistent with the existing admin-configured `Ftp` section. |
| A protocol is added later and the connector rows for the old protocols change meaning | Prevented by construction: the type row names the protocol, and the settings column is keyed by field name (research D1, D3). |
| The FTP client seam is shared with the custom-model deployer, so a change there affects both | The move is a namespace change plus three added operations; the existing uploader's behaviour and tests are unchanged (research D11). |
| A connector's credential cannot be decrypted after a key-ring change | The failure surfaces as a 422 with `CredentialUnreadable`-style reason, never as a silent empty credential. The `DeploymentConnectorCredentialProtector` uses a dedicated purpose, so it is affected only by a key-ring change affecting that purpose. |
| The two names are unique platform-wide, so a second administrator cannot reuse "Production" | Intended (clarification): one shared list, like Voice and AI providers. A deleted name is released. |
| The save is refused when the target is temporarily unreachable | Intended (FR-009): a connector that is saved is known to work. The administrator can still rename it, because a rename does not re-test. |

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| A JSON settings column instead of a column per protocol field | The protocol's extra fields are genuinely per-transport, and the type must be able to define the connector's schema (FR-002). | One column per field needs a migration for every new transport and leaves most columns NULL for any given row (research D3). |
| A separate permission area instead of reusing `admin.ai-providers.*` | A connector holds a server credential for a file-transfer host — a different blast radius from a model vendor key (FR-012). | Reusing the AI-provider permission would grant deployment credentials to a role that was granted model vendor access (research D10). |
| A per-transport `IConnectorProtocol` interface rather than one interface with a `switch` | Each transport validates and connects differently; a switch would grow a branch per transport and the schema would no longer be declarable (FR-002, research D1, D2). | A single switch-based tester cannot express "this protocol declares these fields" without becoming a type-per-protocol anyway. |
