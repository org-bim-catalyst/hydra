# Feature Specification: Deployment Connectors Administration

**Feature Branch**: `071-deployment-connectors`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Add a \"Connectors\" tab to the Admin panel — reusable deployment
targets that other admin features (starting with a custom-model downloader) can push files to.
New entity, e.g. DeploymentConnector: name, type (a table for connector types should be created
and managed by admin and appears as a drop down list in the create/edit pages with a \"+\" next to
the drop list to allow the admin to define new types and \"-\" button to remove existing one with
confirmation message, start with Ftp/Ftps — design the table so Sftp/S3/AzureBlob can be added
later without breaking existing rows), host, port, username, credential (encrypted at rest using
the same approach as VoiceProvider's credential — never return the plaintext or ciphertext from
the API, only a hint), remote base path, explicit-TLS/passive-mode flags where FTP-specific. Admin
UI: list connectors, add/edit/delete, a \"Test Connection\" action that logs in and verifies write
access to the remote base path before saving, without persisting a connector that fails the test.
API: CRUD endpoints under something like /api/v1/admin/connectors, gated by a permission consistent
with the existing admin.* permission scheme. Credential writes are a separate PUT. This connector
record is a building block only in this feature — do not build the model downloader here."

## Clarifications

### Session 2026-09-23

- Q: Should the system restrict which hosts/ports a connector can target for "Test Connection" (e.g., block internal/private network ranges) to prevent this admin feature from reaching internal-only infrastructure? → A: No restriction — this is an `admin.connectors.manage`-gated feature; admin operators creating connectors are trusted, consistent with how other admin-configured external endpoints already work in this codebase.
- Q: Must connector names and connector type names be unique? → A: Connector type names MUST be unique platform-wide (a type defines the schema/fields for connectors of that type, so duplicates would be ambiguous). Connector names MUST also be unique platform-wide — connectors are a single shared list across the whole admin panel (like Voice/AI Providers), not scoped per admin user; each connector additionally has a unique id.
- Q: When two admins edit the same connector at the same time, what should happen on a conflicting save? → A: The later save is blocked with a conflict message showing the connector's current server-side values; the admin must review those values and reapply their change. No silent overwrite and no automatic merge.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Register a deployment connector (Priority: P1)

An administrator opens **Admin → Connectors** and adds a new deployment target (for example, the
FTP server that will later receive downloaded model files) by giving it a name, a connector type,
host, port, username, credential, and a remote base path. Before the connector is saved, the admin
proves the details are correct by running a connection test.

**Why this priority**: Without a working, verified connector record, no other admin feature (such
as the future model downloader) has anywhere to push files. This is the foundational capability.

**Independent Test**: Can be fully tested by creating a connector against a real or test FTP/FTPS
server, running "Test Connection", and confirming the connector is only saved after the test
succeeds — delivers a trustworthy, reusable deployment target.

**Acceptance Scenarios**:

1. **Given** the admin is on the Connectors tab, **When** they choose "Add Connector" and fill in
   name, type, host, port, username, credential, and remote base path, **Then** the form requires a
   successful "Test Connection" before the "Save" action is enabled.
2. **Given** valid connection details, **When** "Test Connection" is pressed, **Then** the system
   logs in to the remote target and verifies it can write to the configured remote base path, and
   shows a success indicator.
3. **Given** the test succeeds, **When** the admin saves, **Then** the connector appears in the
   connector list with its name, type, host, and a credential hint (never the plaintext or
   encrypted credential value).
4. **Given** invalid or unreachable connection details, **When** "Test Connection" is pressed,
   **Then** the system shows the specific failure reason (for example, authentication rejected, host
   unreachable, or the remote base path is not writable) and the connector is not saved.

---

### User Story 2 - Manage connector types (Priority: P2)

An administrator needs the list of connector types (Ftp, Ftps, and future types) to be extensible
without a code deployment, so they can define a new type as the platform grows, and remove one that
is no longer needed as long as nothing depends on it.

**Why this priority**: This keeps the type list open-ended per the requirement that Sftp, S3, and
Azure Blob can be added later, but it is secondary to being able to create and use a connector at
all with the initial Ftp/Ftps types.

**Independent Test**: Can be fully tested by opening the connector type manager, adding a new type
name, confirming it now appears in the type dropdown on the connector form, then removing an unused
type after confirming a warning prompt.

**Acceptance Scenarios**:

1. **Given** the connector create/edit form, **When** the admin selects the type dropdown, **Then**
   they see all currently defined connector types (starting with Ftp and Ftps).
2. **Given** the "+" control next to the type dropdown, **When** the admin defines a new type name,
   **Then** it is added to the list of available types and immediately selectable.
3. **Given** the "-" control next to the type dropdown, **When** the admin chooses to remove a type,
   **Then** a confirmation message is shown before removal.
4. **Given** a connector type that is currently used by one or more existing connectors, **When**
   the admin tries to remove it, **Then** the system blocks the removal and explains that the type
   is in use.

---

### User Story 3 - Maintain existing connectors (Priority: P2)

An administrator reviews the list of configured connectors, edits one whose host or remote path
changed, updates a connector's credential on its own without touching other fields, or deletes a
connector that is no longer needed.

**Why this priority**: Connectors are long-lived configuration; keeping them accurate and letting
credentials rotate independently of other fields is necessary for ongoing operation, but it builds
on Story 1 rather than blocking it.

**Independent Test**: Can be fully tested by editing a saved connector's non-credential fields,
separately rotating its credential, and deleting a connector, then confirming the list reflects
each change.

**Acceptance Scenarios**:

1. **Given** a saved connector, **When** the admin opens it for editing and changes the host, port,
   username, remote base path, or connector-type-specific flags, **Then** those changes are saved
   without requiring or exposing the existing credential.
2. **Given** a saved connector, **When** the admin chooses to update only its credential, **Then**
   the new credential is submitted through a separate action from the general edit, and the response
   never contains the plaintext or encrypted value — only an updated hint.
3. **Given** a saved connector, **When** the admin deletes it, **Then** it is removed from the list
   and is no longer available to any feature that references connectors.
4. **Given** the connector list, **When** it is displayed, **Then** each entry shows name, type,
   host, and a non-sensitive credential hint, but never the credential itself.

---

### Edge Cases

- What happens when an admin tries to save a connector after editing its details post-test (for
  example, changing the host after a successful test)? The system MUST require the test to be
  re-run against the current details before saving.
- What happens when the remote base path exists but is read-only for the given credentials? The
  test MUST fail with a reason that distinguishes "cannot log in" from "logged in but cannot write."
- What happens when an admin removes a connector type that has no existing connectors but a
  downloader or other feature might reference it in the future? Removal is allowed once nothing
  currently uses it; the system does not need to guess about future use.
- What happens when two admins edit the same connector at the same time? The later save MUST be
  blocked with a conflict message that shows the connector's current server-side values, so the
  admin can review them and reapply their change — never a silent overwrite or automatic merge.
- What happens when a connector's stored credential can no longer be decrypted (e.g., after a key
  rotation issue)? Any action needing the credential MUST fail with a clear, caught error rather
  than a silent failure, consistent with this project's no-silent-failures standard.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow administrators to create a deployment connector with: a name, a
  connector type, host, port, username, a credential, and a remote base path. Connector names MUST
  be unique platform-wide, in a single shared list visible to every administrator (not scoped per
  admin user), consistent with how Voice and AI Providers are shared admin-wide configuration.
- **FR-002**: System MUST support connector-type-specific options, starting with explicit-TLS and
  passive-mode flags for Ftp/Ftps connectors, without requiring those fields for future non-FTP
  types.
- **FR-003**: System MUST maintain connector types as administrator-managed data (not a fixed code
  list), pre-populated with Ftp and Ftps, so that additional types (e.g., Sftp, S3, Azure Blob) can
  be added later without changing or migrating existing connector records. Connector type names
  MUST be unique platform-wide.
- **FR-004**: System MUST let administrators add a new connector type by name from the connector
  create/edit form, via a "+" control next to the type selector.
- **FR-005**: System MUST let administrators remove a connector type via a "-" control next to the
  type selector, only after an explicit confirmation, and MUST prevent removal of any type that is
  currently used by one or more connectors.
- **FR-006**: System MUST encrypt the connector credential at rest using the same protection
  approach already used for the Voice Provider's credential.
- **FR-007**: System MUST NOT return the plaintext or encrypted credential value from any API
  response. Every read of a connector (list or single) MUST expose only a non-sensitive hint.
- **FR-008**: System MUST provide a "Test Connection" action that, using the connector's current
  (possibly unsaved) details, logs in to the remote target and verifies write access to the
  configured remote base path.
- **FR-009**: System MUST reject saving (create or update of connection details) a connector whose
  current details have not passed a "Test Connection" since their last change.
- **FR-010**: System MUST let administrators list, create, edit, and delete deployment connectors.
- **FR-011**: System MUST provide credential updates as an action separate from the general
  connector edit, so that non-credential fields can be changed without resupplying or exposing the
  credential, and credential changes can be made without touching other fields.
- **FR-012**: System MUST gate every connector-management action (view and manage) behind
  administrator permissions, following this platform's existing admin permission scheme (e.g., a
  `admin.connectors.view` / `admin.connectors.manage` style split between read and write access).
- **FR-013**: System MUST record enough information on a saved connector (host, credential
  reference, remote base path, and connector-type-specific flags) that a future feature can open a
  live session to the remote target on the connector's behalf, without this feature needing to
  implement that session itself.
- **FR-014**: System MUST surface a specific, user-visible failure reason whenever a connection
  test or a connector operation fails — no failure may be silently swallowed or left unexplained in
  the admin UI.
- **FR-015**: This feature MUST NOT implement any file-push, model-download, or other consumer of
  connectors; it delivers the connector record and its lifecycle only.
- **FR-016**: System MUST NOT restrict which hosts or ports a connector may target for "Test
  Connection" or future connector use (no internal/private-network blocking). Administrators
  holding `admin.connectors.manage` are treated as trusted operators, consistent with how other
  admin-configured external endpoints already work in this codebase.
- **FR-017**: System MUST detect when a connector has been modified since an administrator loaded
  it for editing, and MUST block a conflicting save with the connector's current server-side values
  shown to the admin, rather than overwriting the intervening change silently or merging it
  automatically.

### Key Entities *(include if feature involves data)*

- **Deployment Connector**: A reusable, named target — unique id, unique name platform-wide — that
  other admin features can push files to. Holds a connector type, host, port, username, an
  encrypted credential (exposed only as a hint), a remote base path, and connector-type-specific
  settings (e.g., explicit TLS and passive mode for FTP-family types). Must remain valid (tested)
  for its currently saved connection details. Belongs to one shared, admin-wide list, not scoped
  per admin user.
- **Connector Type**: An administrator-managed, extensible classification for connectors (starting
  with Ftp and Ftps), identified by a platform-wide unique name. Determines which connector-type-
  specific fields apply and cannot be removed while any connector still references it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can register a working deployment connector, from opening the
  Connectors tab to seeing it appear in the list, in under 3 minutes.
- **SC-002**: 100% of connectors visible in the admin list have passed a connection test with their
  currently saved details — no untested or since-modified-but-unverified connector can exist.
- **SC-003**: No API response inspected during testing ever contains a connector's plaintext or
  encrypted credential value — only a hint is present in 100% of read responses.
- **SC-004**: Administrators can rotate a connector's credential without needing to re-enter or
  re-verify any of its other fields, and without any other admin losing access to view that
  connector's non-sensitive details during the rotation.
- **SC-005**: When a connection test fails, the administrator can identify whether the problem was
  reaching the host, authenticating, or writing to the remote path, without needing to consult logs
  outside the admin UI.

## Assumptions

- The credential encryption approach mirrors the existing Voice Provider credential handling
  (encrypted at rest, hint-only on read) rather than introducing a new secret-storage mechanism.
- The initial connector types are Ftp and Ftps; other types named in the request (Sftp, S3, Azure
  Blob) are anticipated future additions and are out of scope for this feature's UI/testing logic
  beyond ensuring the type list and connector schema do not need to change shape to accommodate
  them later.
- "Verifies write access to the remote base path" means the test performs a non-destructive proof
  of write capability (for example, writing and removing a small marker) rather than leaving
  artifacts behind on the remote target.
- Connector permissions follow the same two-tier (view/manage) pattern already used for admin AI
  providers and voice providers, rather than introducing a new permission shape.
- The future model-downloader feature (and any other consumer of connectors) is explicitly out of
  scope; this feature only needs to leave behind what such a feature would need to read from a
  connector (host, credential reference, remote base path, and protocol-specific flags).
- Only administrators with connector permissions interact with this feature; there is no
  end-user-facing surface.
