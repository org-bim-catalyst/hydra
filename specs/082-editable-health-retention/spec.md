# Feature Specification: Editable Provider Health-Check Retention

**Feature Branch**: `082-editable-health-retention`

**Created**: 2026-10-18

**Status**: Implemented 2026-10-19; verified against a live host 2026-10-24

**Input**: User description: "Make the provider health-check retention window editable from the Admin Panel. Add a singleton setting row (RetentionDays, default 30) with admin GET/PUT using If-Match rowVersion, validation limits, and audit logging. Seed it from the current ProviderHealthCheck:RetentionDays value in the migration, and keep config as the fallback. The retention job reads the stored value. Add the edit control to the existing 'Provider health' page."

## Context

The Admin panel already has a **Provider health checks** page (`/admin/provider-health`) that shows how long provider health-check history is kept. It is read-only: the number comes from deployment configuration (`ProviderHealthCheck:RetentionDays`, default 30 days), so changing it — even by a day — requires a configuration change and a redeployment.

The daily retention job that deletes old health-check rows reads the same configuration value. This feature moves the retention window into an editable, stored setting while keeping configuration as the fallback, so an administrator can change it from the existing page and the next scheduled cleanup uses the new value without a deployment.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Change the retention window from the Admin panel (Priority: P1)

An administrator opens the existing Provider health checks page. Instead of a read-only sentence, they see the current retention window with an edit control. They enter a new number of days, save, and the page confirms the new value. The saved value is what the page shows from then on, for every administrator, until it is changed again. No deployment or configuration change is involved.

**Why this priority**: This is the whole feature. Everything else (the cleanup job honouring the value, the audit trail) exists to support this one admin action.

**Independent Test**: Sign in as an administrator, open `/admin/provider-health`, change the retention window from 30 to a different number within the allowed range, save, reload the page, and confirm the new value is shown. Confirm a non-administrator can neither see nor use the edit control.

**Acceptance Scenarios**:

1. **Given** an administrator on the Provider health checks page, **When** the page loads, **Then** it shows the effective retention window (the stored value if one has been saved, otherwise the configured fallback) and the check interval, as it does today.
2. **Given** an administrator who enters a valid number of days, **When** they save, **Then** the page shows a success confirmation and the new value, and the value survives a page reload.
3. **Given** an administrator who enters a value outside the allowed range, **When** they try to save, **Then** the save is rejected and the page explains the allowed range and shows no success confirmation.
4. **Given** an administrator who leaves the field empty or enters something that is not a whole number, **When** they try to save, **Then** the save is rejected with a clear explanation and the previously saved value is unchanged.
5. **Given** a user without the required administrative permission, **When** they open the page or try to change the value, **Then** they cannot view the setting or save a change.
6. **Given** a read-only inspection of the release, **When** the feature is deployed, **Then** the stored setting already holds the value that was in effect before the release (currently 30 days), so no deployment's retention behaviour changes unintentionally.

---

### User Story 2 - The cleanup job uses the saved value (Priority: P2)

The daily cleanup job that deletes old provider health-check history uses the saved retention window on its next run, without a redeployment. Shortening the window means older history disappears at the next run; lengthening it means history is kept longer from the next run onward. If the stored setting is somehow missing, the job falls back to the configured value so cleanup never stops working.

**Why this priority**: The edit is pointless if the job ignores it; but it only takes effect on a scheduled run, so it is secondary to the editable control itself.

**Independent Test**: Save a retention window of one day, wait for (or trigger) the next cleanup run, and confirm that health-check rows older than one day are removed while newer rows remain. Delete the setting row directly, run the job again, and confirm it uses the configured fallback value instead of failing.

**Acceptance Scenarios**:

1. **Given** a saved retention window of N days, **When** the daily cleanup runs, **Then** it deletes health-check rows recorded more than N days before the run and keeps rows within the window.
2. **Given** an administrator changes the window, **When** the next cleanup run happens, **Then** it uses the newly saved value — no redeployment is needed.
3. **Given** an administrator changes the window, **When** the change is saved, **Then** no rows are deleted immediately; deletion happens only on the next scheduled run.
4. **Given** the stored setting cannot be read (for example, the row is missing), **When** the cleanup runs, **Then** it uses the configured fallback value and still completes.
5. **Given** existing deployments where the setting has never been changed, **When** the feature is first deployed, **Then** cleanup behaviour is identical to before the release.

---

### User Story 3 - Every change is auditable (Priority: P3)

Every saved change to the retention window is recorded in the administrative audit trail with who changed it, when, and the value before and after. A rejected save is not recorded as a change.

**Why this priority**: Retention controls how long operational history survives. The audit trail is required for governance, but the feature is usable without reading it.

**Independent Test**: Change the retention window, then open the administrative audit trail and confirm one entry names the administrator, the previous value and the new value. Attempt an invalid save and confirm no new entry is written.

**Acceptance Scenarios**:

1. **Given** an administrator saves a new retention window, **When** the change succeeds, **Then** an audit entry records the administrator, the timestamp, the previous value and the new value.
2. **Given** an administrator attempts an invalid or conflicting save, **When** it is rejected, **Then** no successful-change audit entry is written.
3. **Given** two administrators edit the page, **When** the second saves using a version of the setting read before the first saved, **Then** the second save is rejected as conflicting and the page explains that the setting changed and must be reloaded.

---

### Edge Cases

- **Concurrent edits**: One administrator saves while another still has the old value on screen. The stale save must be rejected and must never silently overwrite the newer value.
- **Boundary values**: The smallest and largest allowed values must be accepted and saved exactly; one below or above must be rejected.
- **First save after upgrade**: The seeded value must be present, so the page shows a stored value rather than an "unset" state.
- **Missing or unreadable setting**: The page shows the configured fallback and marks the value as coming from configuration; because there is no stored version to guard a change, saving is disabled until the setting row exists (the migration seeds it). The cleanup job uses the fallback rather than failing.
- **Shortened window**: If an administrator shortens the window below the age of existing rows, those rows are deleted at the next run, not at save time.
- **A failed cleanup run**: The next scheduled run tries again; a failed run does not change the stored setting.
- **Repeated saves of the same value**: Saving the current value succeeds without an error (or is clearly reported as needing no change), and does not corrupt the setting.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST store the provider health-check retention window as a single platform-wide setting, defaulting to 30 days.
- **FR-002**: The stored setting MUST be seeded with the retention value in effect before this feature (the configured value, or 30 days when none is configured), so the release does not change existing retention behaviour.
- **FR-003**: Administrators with the appropriate permission MUST be able to read the effective retention window, including whether it comes from the stored setting or the configuration fallback.
- **FR-004**: Administrators with the appropriate permission MUST be able to change the retention window from the existing Provider health checks page.
- **FR-005**: The system MUST reject retention values outside the allowed range (minimum 1 day, maximum 365 days) and non-whole-number values, with a clear explanation of the allowed range.
- **FR-006**: The system MUST reject a save that is based on a stale view of the setting, using the version last read by the administrator, and MUST tell the administrator to reload before retrying.
- **FR-007**: A requested change MUST require that the version last read by the administrator be supplied with the save (the `If-Match` row-version pattern already used by other single-row admin settings).
- **FR-008**: Every successful change MUST be recorded in the administrative audit trail with the actor, timestamp, previous value and new value. Rejected saves MUST NOT be recorded as successful changes.
- **FR-009**: The daily retention job MUST use the stored value when present and MUST fall back to the configured `ProviderHealthCheck:RetentionDays` value (default 30) when the stored value cannot be read.
- **FR-010**: A saved change MUST take effect on the next scheduled cleanup run without a redeployment, and MUST NOT trigger an immediate deletion.
- **FR-011**: The Provider health checks page MUST show the current effective value, provide the edit control and its allowed range, surface validation and conflict errors visibly, and confirm a successful save.
- **FR-012**: The system MUST keep the existing check interval information on the page unchanged.
- **FR-013**: Settings reads and changes MUST be restricted to administrators holding the operational-failures view and manage permissions respectively; unauthenticated and unauthorised callers MUST be refused.

### Key Entities

- **Provider health retention setting**: A single, platform-wide record holding the number of days health-check history is kept, the version used to detect concurrent edits, and who/when it was last changed. It does not belong to any user or organisation.
- **Provider health check record** (existing): A timestamped record of one provider health probe. Its age, relative to the retention window, determines whether the cleanup job deletes it.
- **Provider health retention audit entry**: An append-only governance record of a successful settings change, capturing actor, time, previous value and new value. It is never updated or deleted by the feature.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can change the retention window and see it confirmed in under one minute, with no deployment, configuration file edit or service restart.
- **SC-002**: 100% of successful retention changes appear in the audit trail within the same request, with actor and before/after values.
- **SC-003**: 100% of stale concurrent saves are rejected; no silent overwrite occurs.
- **SC-004**: 100% of values outside 1–365 days, and all non-numeric input, are rejected before the setting changes.
- **SC-005**: The first cleanup run after a saved change uses the new value; zero runs use the previous value after the change has been saved.
- **SC-006**: With the stored setting unavailable, the cleanup job still completes using the configured fallback in 100% of runs.
- **SC-007**: Retention behaviour on the release that introduces this feature is identical to the prior release for deployments that never edit the setting.
- **SC-008**: The existing Provider health checks page remains readable to administrators with view-only access, showing the effective value without exposing the edit control.

## Assumptions

- **Allowed range**: 1 day minimum and 365 days maximum. These bounds keep the setting meaningful (at least one day of history) and bounded (no accidental permanent retention). They were chosen as reasonable defaults; only the range constants need to change if a different limit is wanted.
- **Permissions**: Reading uses the existing operational-failures view permission (as today); changing uses the existing operational-failures manage permission. No new permission key is introduced.
- **Fallback semantics**: "Configured fallback" means the existing `ProviderHealthCheck:RetentionDays` configuration value, defaulting to 30 days. Config no longer overrides a stored value; it only applies when the stored row is missing or unreadable.
- **Seed value**: The migration seeds the current effective value. No `ProviderHealthCheck:RetentionDays` override exists in the repository, so the seed is 30; a deployment with an override gets that value seeded instead if the migration is generated to carry it.
- **Effective timing**: The cleanup job runs daily; a saved change is picked up on its next run. Immediate re-deletion on save is explicitly out of scope.
- **Single tenant-wide setting**: There is one retention window for the whole platform, not one per provider, user or organisation.
- **Existing page**: The "Provider health" page referred to is the existing `/admin/provider-health` page titled "Provider health checks"; the check-interval display and any other content on it stay as they are.
- **Audit mechanism**: The change is recorded in a small append-only audit table dedicated to this setting, following the platform's existing per-area audit-log pattern. No audit-reading UI or export is added; the notification and role audit trails already serve their own areas.
- **No change to what is stored**: Health checks keep their current contents and deletion is still age-based on the recorded check time.
