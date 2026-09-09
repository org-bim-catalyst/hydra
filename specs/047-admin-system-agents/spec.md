# Feature Specification: Admin Visibility of System Agents

**Feature Branch**: `047-admin-system-agents`

**Created**: 2026-09-09

**Status**: Draft

**Input**: User description: "Admin visibility of system agents: allow a Super User/Administrator to view the system-provisioned agents (e.g. lucy.orchestrator, created via specs/045's SystemAgentProvisioner with OwnerId=\"system\") in a dedicated admin view, since the existing personal Agents page (GET /agents) is deliberately scoped to the caller's own agents (OwnerId == current user) per FR-048 and can never show system-owned agents. This is a read-only visibility feature — list system agents, show their versions/status/last-provisioned info, reuse the existing isSystemOwned read-only badge pattern from the Agent Library UI (T108) — mirroring the existing admin pattern already used for AI providers (/admin/ai-providers). No changes to the personal Agents page's existing user-scoping behavior."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See which system agents exist and their current state (Priority: P1)

An administrator wants to confirm that the platform's built-in agents (such as the conversational orchestrator that powers every chat turn) were actually provisioned successfully, without needing database access. They open a dedicated admin screen and see every system agent, its current published version, and when it was last provisioned or updated.

**Why this priority**: Without this, the only way to confirm system-agent provisioning succeeded is to query the database directly — there is currently no in-product way to verify these agents exist, which blocks routine operational checks (e.g., after a deploy or environment migration).

**Independent Test**: Can be fully tested by signing in as an Administrator, navigating to the new admin screen, and confirming it lists every currently-provisioned system agent with a name, status, and version — delivers value on its own with no other story required.

**Acceptance Scenarios**:

1. **Given** the platform has successfully provisioned its system agents, **When** an Administrator opens the System Agents admin screen, **Then** they see one row per system agent showing its name, status, current published version number, and the timestamp of its most recent provisioning/update.
2. **Given** a system agent's definition changed and a new version was published, **When** the Administrator views that agent's row, **Then** the version number and last-updated timestamp reflect the newest published version, not an earlier one.
3. **Given** no system agents have been provisioned yet (e.g., a brand-new environment where the provisioning step has not completed), **When** an Administrator opens the screen, **Then** they see a clear empty state explaining that no system agents are currently provisioned, rather than an error or a blank page.

---

### User Story 2 - Distinguish system agents from user agents at a glance (Priority: P2)

An administrator viewing the system agents screen needs to be confident these are platform-managed, not something a user created, so they don't mistake one for editable content or try to modify it from here.

**Why this priority**: Prevents confusion between this admin-only, read-only view and the personal Agent Library where users build their own agents — important for trust in the data shown, but secondary to simply being able to see the list at all (Story 1).

**Independent Test**: Can be fully tested by opening the screen and confirming every row is visibly marked as system-owned/read-only, with no controls that imply editing, deleting, or duplicating are possible from this screen.

**Acceptance Scenarios**:

1. **Given** the Administrator is viewing the System Agents screen, **When** they look at any row, **Then** it is clearly marked as a system-owned, read-only agent (the same visual treatment already used elsewhere in the product for system-owned agents).
2. **Given** the Administrator is viewing the System Agents screen, **When** they look for edit, delete, duplicate, or publish controls, **Then** none are present — the screen is view-only.

---

### User Story 3 - Only administrators can see this screen (Priority: P1)

A non-administrator user (a regular chat user, or one without the Super User/Administrator role) must not be able to reach this screen or its underlying data, since it exposes platform-internal configuration that is not relevant or appropriate for ordinary users.

**Why this priority**: This is an access-control requirement with security implications; it must hold from the first release of the feature, not be added later — grouped at P1 alongside Story 1.

**Independent Test**: Can be fully tested by attempting to reach the screen (and its underlying data request) as an anonymous user and as an authenticated non-administrator user, and confirming both are denied.

**Acceptance Scenarios**:

1. **Given** an anonymous (unauthenticated) visitor, **When** they attempt to load the System Agents admin screen or its underlying data, **Then** access is denied and they are not shown any system agent data.
2. **Given** an authenticated user without the Administrator or Super User role, **When** they attempt to load the System Agents admin screen or its underlying data, **Then** access is denied and they are not shown any system agent data.
3. **Given** an authenticated user with the Administrator or Super User role, **When** they load the screen, **Then** access is granted and the data loads normally.

### Edge Cases

- What happens when system-agent provisioning is still pending (e.g., a database migration has not yet completed) and no system agents exist yet? → Covered by Story 1, Scenario 3 (empty state, not an error).
- What happens if a system agent was archived rather than currently published? → Its status is shown as-is (e.g., archived) rather than hidden, so an administrator can see the full lifecycle state, not just active agents.
- How does the screen behave if there are many system agents in the future? → The list must remain usable (readable, no pagination failure) as the number of system agents grows beyond what fits on one screen.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a way for a user with the Administrator or Super User role to view a list of every system-owned agent, independent of and without affecting the existing personal Agents list (which remains scoped to the caller's own agents).
- **FR-002**: For each system agent, the system MUST display at minimum: its name, its lifecycle status (e.g., draft/published/archived), its current version number, and the timestamp of its most recent provisioning or version update.
- **FR-003**: The system agents list MUST be presented as read-only — no create, edit, delete, duplicate, or publish actions are available from this view.
- **FR-004**: Every system agent shown MUST be visually and unambiguously distinguished as system-owned, reusing the existing system-owned indicator already used in the Agent Library.
- **FR-005**: System MUST deny access to this view and its underlying data to any user who is not authenticated as an Administrator or Super User, including anonymous visitors and authenticated non-admin users.
- **FR-006**: System MUST display a clear, non-error empty state when no system agents currently exist (e.g., provisioning has not yet run).
- **FR-007**: The existing personal Agents list (scoped to the caller's own agents) MUST continue to exclude system-owned agents exactly as it does today — this feature adds a new, separate view rather than modifying that one.

### Key Entities

- **System Agent**: A platform-provisioned agent (owned by the platform itself, not an end user) that powers built-in conversational capability. Relevant attributes for this view: name, lifecycle status, current published version, last provisioning/update timestamp, and its system-owned marker.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An administrator can determine, within 10 seconds of opening the product, whether the platform's system agents exist and are in a healthy (published) state — without needing any tool or access outside the product itself.
- **SC-002**: 100% of attempts to reach this view or its data by non-administrator users (anonymous or authenticated) are denied.
- **SC-003**: The list correctly reflects the current provisioned state of every system agent, including after a new version of a system agent's definition has been published.

## Assumptions

- "Administrator" and "Super User" refer to the two existing administrative roles already used elsewhere in the product's admin screens (the same roles that gate `/admin/ai-providers` today); no new role is introduced.
- This feature only concerns visibility of system agents' metadata (name, status, version, timestamps). Viewing the full configuration/instructions of a system agent, or its execution history, is out of scope for this feature and may be considered separately later.
- The number of system agents is expected to remain small (today: one, `lucy.orchestrator`) for the foreseeable future; the list does not need to support large-scale pagination, though it must not break if the count grows moderately.
- No changes are made to how or when system agents are provisioned (the existing `SystemAgentProvisioner` behavior is unaffected) — this feature only adds a way to observe the result.
