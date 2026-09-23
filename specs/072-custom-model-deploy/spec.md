# Feature Specification: Custom Model Deployment (Admin)

**Feature Branch**: `072-custom-model-deploy`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Add a \"Custom Models\" capability to the Admin panel that downloads a
model directly on the server (not through the admin's browser) and pushes it to production over
FTP. Skip building a Connectors/DeploymentConnector abstraction for now — read FTP settings directly
from configuration instead, and structure the code so that swap is a one-file change later. [...]
Source: a Hugging Face repo URL, optionally with a /tree/<revision> or /resolve/<revision> segment.
Destination: a relative path joined with the configured FTP root. Live per-file and overall progress,
a cancel action, a size cap, SSRF and path-traversal guardrails, and no silent failures." (Full text
in the `/speckit-specify` invocation of 2026-09-23.)

**Relationship to spec 071**: `specs/071-deployment-connectors` specified admin-managed deployment
targets. This feature deliberately does **not** depend on it. The one deployment target comes from
server configuration. Replacing that source with a connector record later must touch only the piece
that supplies the target's settings (see FR-016).

## Clarifications

### Session 2026-09-23

- Q: Who may deploy to production — a dedicated permission pair or the existing AI-providers pair?
  → A: A dedicated pair, `admin.custom-models.view` / `admin.custom-models.manage`. The built-in
  privileged roles get it automatically, as they do every permission. Custom roles get it only when
  explicitly granted. It is never implied by `admin.ai-providers.*`.
- Q: Must the connection to the deployment server be encrypted? → A: Explicit TLS (FTPS) is required
  by default. Plain FTP is allowed only when a configuration flag (`AllowPlainFtp`, default `false`)
  is set. There is never a silent fall back to plain FTP.
- Q: What happens when files already exist at the destination? → A: They are always overwritten
  with no confirmation step. Each job records and reports exactly which remote files it overwrote,
  both live while it runs and on the finished job.
- Q: Is the page only a deployment history, or does it manage the deployed models? → A: It manages
  them. **Custom Models** is a second section on **Admin → AI Providers**, below the frontier
  providers (Anthropic, Gemini, OpenAI, OpenRouter). Instead of frontier models (GPT-5, etc.) it
  lists server-hosted models (for example Supertonic, Whisper). Frontier models are added with "Sync
  from provider"; custom models are added with **Add model**, which deploys the files. Each deployed
  model appears in the list, and the administrator chooses whether it is Available or Unavailable.
- Q: What does "Available" do at runtime? → A: It is connected now. The on-server voice engine
  (Supertonic) runs only from an Available custom model: the Voice page's "+" dialog offers it only
  when its custom model is Available, and it loads its files from that model's deployed location.
  Speaking, transcribing and chatting will later become capabilities that any model (frontier or
  custom) can provide. That redesign, and wiring transcription (Whisper), are for a later spec.
- Q: (from /speckit-analyze, H1) Which custom model records take the engine off its configured
  folder? → A: Only a **Completed**, non-removed one. A Queued, running, Failed or Cancelled
  deployment of the engine's repository leaves the engine on its existing configured folder, so a
  failed first deploy never silences a working voice (FR-039).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Deploy a model from a Hugging Face repository (Priority: P1)

An administrator opens **Admin → AI Providers**. Below the frontier providers (Anthropic, Gemini,
OpenAI, OpenRouter) is a **Custom Models** section listing the models hosted on Lucy's own server
(for example Supertonic or Whisper). They press **Add model** and paste a
Hugging Face repository address (for example `https://huggingface.co/Supertone/supertonic-3`) and
type a destination folder relative to the deployment root (for example `Models/supertonic-3`). The
server fetches every file in the repository and uploads each one to the production file server under
that folder. The administrator's browser never downloads the model bytes.

**Why this priority**: This is the whole feature. Today, installing a model on production (for
example the Supertonic voice model from spec 070) means downloading gigabytes to a workstation and
uploading them again by hand.

**Independent Test**: Submit a small public repository to a test file server. Confirm that every
file in the repository is at `{root}/{destination}/{relative path}` with the same byte size, and
that the job ends in the Completed state.

**Acceptance Scenarios**:

1. **Given** the Custom Models section, **When** the administrator presses "Add model", **Then** a
   dialog asks for two required fields: Source and Destination.
2. **Given** a valid source `https://huggingface.co/{owner}/{repo}`, **When** the administrator types
   it, **Then** the dialog shows `{repo}` as the model name without the administrator typing one.
3. **Given** a source with `/tree/{revision}` or `/resolve/{revision}`, **When** the job runs,
   **Then** the files are taken from that revision. Without a revision segment, the repository's
   default revision is used.
4. **Given** a valid submission, **When** the job completes, **Then** every repository file is on the
   production file server at `{configured root}/{destination}/{relative file path}`, with any missing
   folders created, and the job shows as Completed.
5. **Given** the destination `Models/supertonic-3` and a configured root of `/hydra`, **Then** the
   files land under `/hydra/Models/supertonic-3/`. The administrator never sees or types the root.
6. **Given** a valid submission, **When** the dialog closes, **Then** the new model is immediately a
   row in the Custom Models list. It shows its name, source repository and revision, destination,
   and deployment state, and its live progress is shown in that row.

---

### User Story 2 - Watch progress live and cancel (Priority: P1)

After submitting, the administrator watches a progress display with an overall bar and a bar for the
current file (bytes transferred and percentage). The display updates as the transfer happens. The
administrator can cancel a job that is in progress.

**Why this priority**: Model downloads run for many minutes. Without live progress, a stalled job and
a slow job look the same. That breaks the platform's "no silent failures" rule.

**Independent Test**: Start a multi-file job and confirm that both bars move without a page reload.
Cancel it partway and confirm that the job ends as Cancelled and that no temporary files remain on
the server.

**Acceptance Scenarios**:

1. **Given** a running job, **Then** the page shows the overall bytes and percentage, the name of the
   current file, that file's bytes and percentage, and the file count (for example "4 of 12").
2. **Given** a running job, **When** the administrator reloads the page or opens it in another tab,
   **Then** the job's current state and progress are shown again. Progress is not lost.
3. **Given** a running job, **When** the administrator presses Cancel and confirms, **Then** the job
   stops within a few seconds, shows Cancelled, and all its temporary files on the server are
   removed.
4. **Given** a job that finished (Completed, Failed, or Cancelled), **Then** its model stays in the
   Custom Models list with its final deployment state, total size, destination, and the time it
   finished.
5. **Given** some repository files already existed at the destination, **When** the job overwrites
   them, **Then** each overwritten file is flagged as it is transferred. The finished job shows the
   number of overwritten files and lists their remote paths, and each entry keeps the file's size
   before it was overwritten. A job that overwrote nothing says "No existing files were
   overwritten".

---

### User Story 3 - Unsafe or oversized requests are rejected (Priority: P1)

A source that is not a Hugging Face repository is refused. So is a destination that would write
outside the deployment root, and a repository larger than the size limit. Each refusal comes with a
clear reason, and nothing is fetched or written.

**Why this priority**: An administrator-supplied address makes the server fetch it and write to a
remote file server. Without these checks, the feature could reach internal network addresses or
overwrite arbitrary files on production. The endpoint being admin-only does not remove that risk.

**Independent Test**: Submit each rejected input from the Edge Cases list. Confirm that each one is
refused with a specific reason, and that no outbound fetch or remote write happens.

**Acceptance Scenarios**:

1. **Given** a source whose host is not `huggingface.co`, **When** it is submitted, **Then** it is
   refused with "Source must be a huggingface.co model repository" and no job is created.
2. **Given** a destination containing `..`, an absolute path, a drive letter, a backslash, or any
   form that resolves outside the configured root, **When** it is submitted, **Then** it is refused
   with a reason and no job is created.
3. **Given** a repository whose total size is over the configured limit, **When** the job lists the
   repository's files, **Then** it fails before transferring any bytes. The error states the
   repository's size and the limit.
4. **Given** a repository file whose relative path would resolve outside the destination folder,
   **Then** the job fails before transferring any bytes and names the file.

---

### User Story 4 - Failures are visible, never a stall (Priority: P1)

When something fails partway (the file server rejects the login, the connection drops mid-upload,
Hugging Face returns an error, or the server's disk fills), the progress display changes to a
visible Failed state with a readable reason. The job never just stops moving.

**Why this priority**: This is required by the platform's No Silent Failures principle
(constitution §2 VIII).

**Independent Test**: Point the job at a file server with wrong credentials, then at one that drops
the connection mid-transfer. In both cases the job must reach Failed with a specific reason on the
page, the failure must be recorded in the server log, and temporary files must be removed.

**Acceptance Scenarios**:

1. **Given** the file server rejects the login, **Then** the job shows Failed with
   "Deployment server rejected the login". The reason contains no password or other secret.
2. **Given** the connection drops during a file upload, **Then** the job shows Failed and names the
   file that was being transferred.
3. **Given** the repository does not exist, or is private or gated, **Then** the job shows Failed
   and says that the repository was not found or needs access the server does not have.
4. **Given** the deployment settings are missing or incomplete on this server, **Then** the Add
   model action says that deployment is not configured, and no job is accepted.
5. **Given** the server restarts while a job is running, **Then** the job shows Failed
   ("interrupted by a server restart"). It is not re-run silently, and its temporary files are
   removed.

---

### User Story 5 - Choose which custom models are available (Priority: P1)

Once a model has deployed, the administrator decides whether it is available. This works the same
way as the Available / Unavailable status that frontier models already have.

**Why this priority**: Copying files is only half the job. The administrator also needs one place
that lists every server-hosted model and controls whether each can be used, as they already can for
frontier models.

**Independent Test**: Deploy a model and confirm it starts as Unavailable. Make it Available, then
Unavailable again, and confirm each change is kept after a reload and is audited. Confirm a model
whose deployment failed cannot be made Available and can be removed from the list.

**Acceptance Scenarios**:

1. **Given** a model whose deployment just completed, **Then** its availability is Unavailable until
   the administrator changes it. A new deployment never makes a model Available on its own.
2. **Given** a model with a Completed deployment, **When** the administrator marks it Available (or
   Unavailable), **Then** the status changes, stays changed after a reload, and is recorded in the
   audit log.
3. **Given** a model whose deployment is Queued, running, Failed or Cancelled, **Then** the
   Available option is disabled, and the reason is shown ("deployment has not completed").
4. **Given** a model whose deployment Failed or was Cancelled, **When** the administrator chooses
   "Remove from list" and confirms, **Then** the row is removed and its name can be used again.
   Nothing on the deployment server is touched.
5. **Given** the Custom Models section has no models, **Then** it shows an empty state that invites
   the administrator to use "Add model".

---

### User Story 6 - The voice engine runs from its custom model (Priority: P1)

Lucy's on-server voice (Supertonic, spec 070) no longer depends on a hand-copied folder. It runs
from the Supertonic custom model the administrator deployed and made Available. Making that model
Unavailable takes the engine out of use, and Lucy's voice falls over to the next provider.

**Why this priority**: This is what makes Available mean something. Without it, deploying Supertonic
from the admin panel would still leave the voice engine reading a folder nobody manages.

**Independent Test**: Deploy `Supertone/supertonic-3` and make it Available. Confirm that Supertonic
appears in the Voice page's "+" dialog and speaks a preview from the deployed files. Make the model
Unavailable and confirm Supertonic disappears from the "+" dialog, its preview fails with a clear
reason, and Lucy's replies fall over to the next voice provider.

**Acceptance Scenarios**:

1. **Given** no Available custom model backs Supertonic, **When** the administrator opens the Voice
   page's "+" dialog, **Then** Supertonic is not offered.
2. **Given** an Available custom model deployed from Supertonic's repository, **Then** Supertonic is
   offered in the "+" dialog, and when it speaks it loads its files from that custom model's
   deployed location.
3. **Given** Supertonic is already a voice provider, **When** its custom model is made Unavailable,
   **Then** the Voice page marks the Supertonic provider as "model unavailable", Preview reports that
   reason, and Lucy's replies fall over to the next provider (spec 070 FR-004) with the reason logged.
   The Supertonic provider row itself is kept.
4. **Given** one custom model for Supertonic's repository is already Available, **When** the
   administrator tries to make a second one Available, **Then** the change is refused with a message
   naming the model currently in use. Only one custom model can back an engine at a time.
5. **Given** the custom model is Available but its files are missing or unreadable on this server,
   **When** Supertonic is used, **Then** it fails with "model files not found on this server". The
   failure is logged and shown in Preview, and replies fall over to the next provider. It never
   silently produces no audio.

---

### Edge Cases

- A source URL with a trailing slash, query string, fragment, `www.` prefix, or `http://` scheme.
  The canonical form is accepted (`www.` is normalised and the scheme is upgraded to https). Any
  other host is rejected, including `huggingface.co.evil.example`, `evil.example/huggingface.co`,
  user-info tricks (`https://huggingface.co@evil.example`) and IP literals.
- A source that points at a dataset or space (`/datasets/...`, `/spaces/...`) rather than a model is
  rejected as "not a model repository".
- A source with only an owner (`https://huggingface.co/Supertone`) is rejected, because no model
  name can be derived from it.
- A source with `/blob/{revision}/{file}` or `/resolve/{revision}/{file}`: the revision is used and
  the whole repository is deployed. The trailing file path is ignored, and the dialog says so.
- A revision name containing `/` (for example `refs/pr/3`) is accepted.
- The derived name matches a model already in the Custom Models list: the Name field appears and
  must be filled with a name that is not in use. Names are unique across the list, ignoring case.
- Destination edge cases: empty, only slashes, leading `/` (rejected as absolute), `.` segments,
  URL-encoded `%2e%2e`, Unicode look-alike dots, control characters, and characters the file server
  cannot store. All are rejected or normalised before any write.
- Files already present at the destination with the same path are overwritten without asking, and
  every overwrite is reported (FR-010a). Files at the destination that are not in the repository are
  left in place and are not reported.
- A job that fails or is cancelled partway still reports the files it had already overwritten
  before it stopped. The report does not include files it had not reached yet.
- A repository with zero files is refused ("repository contains no files").
- A second job for a destination that already has a job running is refused.
- A destination that is the deployment root itself, or that points into the application's own
  deployed files or its publicly served folder, is refused. On production, the deployment root is the
  application's own folder.
- A repository file named `web.config` or `app_offline.htm` fails the job before any bytes are
  transferred. Both names change the web server's behaviour when placed on the production site.
- There is no record at all for Supertonic's repository (as on servers set up by hand under spec
  070): Supertonic keeps using its existing configured model folder, so current installations keep
  working. As soon as a custom model record exists for that repository, its availability decides.
- The server's temporary storage runs out mid-download: the job fails with a disk-space reason and
  removes its temporary files.
- Hugging Face rate-limits or is temporarily unavailable: the job retries a bounded number of times
  and then fails with the upstream reason. It never waits forever.
- The administrator closes the browser mid-job: the job keeps running on the server, and reopening
  the page shows its current state.

## Requirements *(mandatory)*

### Functional Requirements

**Submission and validation**

- **FR-001**: Administrators MUST be able to submit a deployment job with two required inputs:
  Source (a Hugging Face model repository address) and Destination (a path relative to the
  deployment root).
- **FR-002**: The system MUST accept a Source only if its scheme is http or https and its host,
  after normalisation, is exactly `huggingface.co`. The path MUST name a model repository
  `{owner}/{repo}`, optionally followed by `/tree/{revision}`, `/resolve/{revision}[/...]`, or
  `/blob/{revision}[/...]`. Every other form MUST be rejected before any network request is made.
- **FR-003**: The system MUST make outbound fetches only to Hugging Face hosts. A fetch that tries to
  redirect anywhere else MUST fail the job and MUST NOT be followed. Hugging Face's own file-delivery
  hosts are the exception, and the plan MUST list them explicitly.
- **FR-004**: The system MUST derive a model name from the repository name and show it in the
  dialog. If no name can be derived, or the derived name is already used by another custom model, a
  required Name field MUST appear.
- **FR-005**: The system MUST reject a Destination that is empty, absolute, contains a `..` segment
  (in any encoding), contains a backslash, drive letter or control character, or resolves outside
  the configured root when joined with it. The check MUST run on the server; a check in the dialog
  alone is not enough.
- **FR-005a**: The destination MUST resolve strictly below the configured root, never to the root
  itself. It MUST NOT overlap the application's own deployed files or its publicly served folder.
  The plan defines the reserved locations, and they MUST be enforced on the server.
- **FR-006**: Every repository file's relative path MUST also be checked so that it resolves inside
  the destination folder. A single violating file MUST fail the whole job before any bytes are
  transferred. So MUST any file named `web.config` or `app_offline.htm` (ignoring case), at any depth.

**Transfer**

- **FR-007**: The system MUST list the repository's files at the requested revision (or the default
  one), including their sizes, before transferring anything.
- **FR-008**: The system MUST refuse a job whose total repository size is over a per-job size limit
  set in server configuration (default 20 GB). The error MUST state both the repository's size and
  the limit.
- **FR-009**: The server MUST download each file to its own temporary storage. Model bytes MUST NEVER
  pass through the administrator's browser.
- **FR-010**: The system MUST upload each file to `{configured root}/{destination}/{relative file
  path}` on the deployment file server, creating missing folders and overwriting files that already
  exist with the same path. There is no confirmation step before overwriting.
- **FR-010a**: Before uploading each file, the system MUST check whether a file already exists at
  its remote path. For every file it overwrites, the system MUST record the remote path (relative to
  the configured root, never the full server path) and the file's size before the overwrite. It MUST
  flag the overwrite in the live progress update for that file, keep the list on the job record, and
  show the count and full list on the finished job, whatever the final state. Each overwrite MUST
  also be written to the audit log.
- **FR-011**: The system MUST confirm that each uploaded file's size on the remote server matches its
  size in the repository. A mismatch MUST fail the job.
- **FR-012**: The job MUST run in the background, independent of the administrator's session, and
  MUST keep running if the browser is closed.
- **FR-013**: The system MUST remove every temporary file for a job when the job completes, fails, or
  is cancelled. A job interrupted by a server restart MUST be marked Failed and its temporary files
  removed the next time the server starts.
- **FR-014**: An interrupted or failed job MUST NOT be retried automatically. The administrator can
  submit a new job.
- **FR-015**: Only one job MAY be running per destination at a time. Destinations are compared
  ignoring case, and a destination inside or containing another running job's destination (for
  example `Models/a` and `Models/a/b`) counts as the same destination.

**Configuration and secrets**

- **FR-016**: The deployment file server's settings (host, port, username, password, root path) MUST
  be read from server configuration through a single, replaceable settings source. Only that source
  may know where the settings come from, so that a future connector-based source can replace it
  without changes to the job, the validation, or the API.
- **FR-017**: Tracked configuration files MUST NOT hold real deployment server values. The example
  configuration files MUST document the settings with placeholder or empty values.
- **FR-018**: The deployment server password MUST NEVER appear in a log entry, an API response, an
  error message shown to the administrator, a progress update, or a persisted job record.
- **FR-018a**: The connection to the deployment server MUST use explicit TLS by default. Plain,
  unencrypted FTP MUST be used only when the deployment settings explicitly allow it
  (`AllowPlainFtp`, default `false`). If TLS cannot be negotiated and plain FTP is not allowed, the
  job MUST fail with a reason that says the server did not accept an encrypted connection. The
  system MUST NEVER fall back from TLS to plain FTP on its own. A certificate the server presents
  that cannot be validated MUST fail the job; it MUST NOT be accepted silently.
- **FR-019**: If the deployment settings are missing or incomplete, the rest of the application MUST
  still start. Only this feature reports "deployment not configured".

**Progress, status and failures**

- **FR-020**: The system MUST push live progress to the administrator's page as the job runs,
  without the page polling. Each update carries the job state, overall bytes and percentage, the
  current file's name, bytes and percentage, and the file count.
- **FR-021**: The system MUST keep a record for each custom model and its deployment (source,
  revision, derived or given name, destination, total size, file count, deployment state, failure
  reason, who submitted it, start and finish times). A page reload or a new tab MUST show current
  state.
- **FR-022**: Job states are Queued, Listing, Transferring, Completed, Failed and Cancelled. Every
  failure MUST end in Failed with a readable reason shown on the page and a matching log entry. A job
  MUST NEVER stay in Transferring after its work has stopped.
- **FR-023**: Administrators MUST be able to cancel a Queued, Listing or Transferring job. A
  cancelled job MUST stop transferring within 5 seconds and end as Cancelled.
- **FR-024**: A failure to deliver the live progress stream to the browser MUST be shown on the page
  (for example "live updates disconnected — reconnecting"). The page MUST NOT freeze on the last
  known value.

**Custom Models list and availability**

- **FR-028**: The AI Providers admin page MUST show a **Custom Models** section below the frontier
  providers. It MUST list every custom model with its name, source repository and revision,
  destination, total size, deployment state, and availability, and show live progress for any row
  that is deploying.
- **FR-029**: The Custom Models section MUST offer **Add model** (FR-001) as the only way to add a
  custom model. "Sync from provider" does not apply to custom models.
- **FR-030**: Each custom model MUST have an availability of Available or Unavailable. It starts
  Unavailable, and it may be made Available only once its deployment has Completed. Changing
  availability MUST require `admin.custom-models.manage` and MUST be audited.
- **FR-031**: A custom model whose deployment Failed or was Cancelled MAY be removed from the list,
  after confirmation. Removing it deletes only the record and never touches files on the deployment
  server. A model with a Completed or running deployment MUST NOT be removable in this feature.
- **FR-032**: Custom model names MUST be unique across the list, ignoring case.

**Engine binding (voice)**

- **FR-033**: An on-server engine MUST declare the Hugging Face repository its model comes from. For
  Supertonic this is `Supertone/supertonic-3`. A custom model backs an engine when it was deployed
  from that repository, whatever the revision. Repository ids are compared ignoring case, and the
  id stored is the one Hugging Face reports, not the casing the administrator typed.
- **FR-034**: At most one custom model may be Available for a given engine repository. Making a
  second one Available MUST be refused, and the message MUST name the one currently in use.
- **FR-035**: The engine MUST load its model files from `{application content root}/{destination}`
  of the Available custom model that backs it. On production, the configured deployment root is the
  application's content root.
- **FR-036**: The Voice page's "+" dialog MUST offer an on-server engine only while an Available
  custom model backs it. Engines that need an API key (for example ElevenLabs) are unaffected.
- **FR-037**: If an existing voice provider's backing custom model becomes Unavailable, or has
  missing or unreadable files, the provider MUST be shown as "model unavailable" on the Voice page.
  Preview MUST report that reason. The voice router MUST treat the provider as failed and fall over
  to the next one before any audio is produced. Each such failure MUST be logged. The provider row
  is not deleted.
- **FR-038**: A change in availability MUST take effect for the next voice request, with no
  application restart. An engine whose model is already loaded in memory MUST stop serving requests
  once its custom model becomes Unavailable.
- **FR-039**: Backward compatibility: until a custom model deployed from an engine's repository has
  reached **Completed** (and has not been removed), the engine MUST keep today's behaviour and load
  from its existing configured model folder. Queued, running, Failed and Cancelled records do not
  count. Once at least one Completed record exists, FR-035 to FR-037 apply.

**Access**

- **FR-025**: Seeing the Custom Models section MUST require `admin.custom-models.view`. Submitting
  or cancelling deployments, changing availability, and removing entries MUST require
  `admin.custom-models.manage`. An administrator with `admin.custom-models.view` but no AI-providers
  permission MUST still be able to open the AI Providers page and see only the Custom Models section. The pair is new, sits in
  the existing admin permission catalogue, and is not implied by any other permission, including
  `admin.ai-providers.*`. The built-in privileged roles get it automatically. A custom role gets it
  only when an administrator grants it.
- **FR-026**: Live progress MUST be delivered only to users who hold the view permission.
- **FR-027**: Every submission, cancellation, completion and failure MUST be recorded as an audit or
  security event that names the acting administrator.

### Key Entities

- **Custom Model**: a model hosted on Lucy's own server, listed in the Custom Models section. Holds:
  a unique name, availability (Available / Unavailable), and its deployment. Separate from the
  frontier-provider model catalogue. It may back an on-server engine through its source repository
  (FR-033).
- **Custom Model Deployment**: the one request that copied the model's Hugging Face repository to
  the deployment server. Holds: source address, repository id, revision, destination (relative),
  deployment state,
  total bytes, transferred bytes, file count, completed file count, current file, failure reason,
  overwritten files (each with its remote path relative to the root and its size before the
  overwrite), submitting administrator, and created, started and finished times. It never holds
  deployment server credentials or the resolved root path.
- **Deployment Target Settings**: the single deployment file server (host, port, username, password,
  root path, and whether plain FTP is allowed), supplied by one replaceable settings source. It is
  not stored as application data in this feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can deploy a public Hugging Face model to production from the admin
  panel in under 1 minute of hands-on time, whatever the model's size, with no files passing through
  their own machine.
- **SC-002**: 100% of the rejected inputs listed in Edge Cases and User Story 3 are refused with a
  specific reason, and no outbound fetch or remote write occurs.
- **SC-003**: Every injected failure (wrong credentials, dropped connection, missing repository,
  full disk, server restart) ends in a visible Failed state with a reason. None leaves the job stuck
  in a running state.
- **SC-004**: The progress display updates at least every 2 seconds while bytes are moving.
- **SC-005**: After any job ends (Completed, Failed or Cancelled), the job's temporary storage on the
  server is empty.
- **SC-006**: A search of logs, API responses and job records for the configured deployment password
  finds zero matches after a full run of the feature's test scenarios.
- **SC-007**: Moving to a future connector-based deployment target changes only the settings source.
  No change is needed to the job, validation, API or UI.
- **SC-008**: An administrator can install Supertonic on production and make it Lucy's voice
  entirely from the admin panel (Add model → Available → Voice "+" → Set as Lucy's voice), with no
  manual file copy.
- **SC-009**: Within one voice request of a Supertonic custom model becoming Unavailable, Lucy's
  replies are spoken by the next provider, and no reply is left silent.

## Assumptions

- The first use is deploying the Supertonic 3 model (spec 070) to production. Deployment targets
  other than the one configured FTP server are out of scope until the Connectors feature (spec 071)
  is built.
- Only public Hugging Face model repositories are supported. Private or gated repositories fail with
  a clear reason. Supporting a Hugging Face access token is a later addition.
- Datasets and Spaces are out of scope. Only model repositories are accepted.
- The whole repository is deployed. Choosing individual files, or filtering by pattern, is out of
  scope.
- Deleting a deployed model's files from the server, and redeploying an existing entry (for example
  to a newer revision), are out of scope for this spec.
- The default size limit is 20 GB per job and can be changed in server configuration.
- Existing files at the destination are overwritten without confirmation, and each overwrite is
  reported (FR-010a). Stale files are never deleted: the feature does not mirror the repository.
  Keeping a backup or a way to roll back overwritten files is out of scope.
- The deployment settings gain one optional field beyond Host, Port, Username, Password and
  RootPath: `AllowPlainFtp` (default `false`). The example configuration files document it.
- Only the on-server voice engine (Supertonic) is connected to custom models in this spec.
  Transcription (Whisper) and other on-server models can be deployed and marked Available, but
  nothing reads their availability yet. A later spec will turn speak, transcribe and chat into
  capabilities that frontier and custom models provide alike, and wire them the same way.
- Jobs are run from the production admin panel, whose database and content root match the
  deployment server. The configured deployment root is production's content root: the CI app deploy
  also writes to `/hydra/`. A custom model recorded in another environment's database refers to
  files that environment does not have locally. There, FR-037 reports "model files not found on
  this server" rather than failing silently.
- The CI app deploy must never delete files under custom model destinations. Its FTP sync only
  removes files it uploaded itself. The plan must verify this, because a deploy that wiped deployed
  models would break the voice engine without warning.
- The development example configuration file already exists. A production example file does not
  exist in the repository today, so one is created with the deployment section and placeholder
  values.
