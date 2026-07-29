# Feature Specification: Admin Dashboard & User Management Console

**Feature Branch**: `001-admin-dashboard`

**Created**: 2026-07-28

**Status**: Draft

**Input**: User description: "Admin Dashboard & User Management Console — the legacy \"ChatGPT Client\" application had an Areas/ControlPanel admin shell (a card-tile home page linking to a Users Manager screen backed by a DataTables grid over raw ApplicationUser records) that was never carried forward into the SPEC-000 migration; today's /admin/users route is only a minimal read-only table (email, name, email-confirmed, 2FA status) with no actions and no landing dashboard. This feature restores and modernizes that admin surface as a proper Control Panel: (1) an Admin Dashboard home showing platform health/usage at a glance via d3.js-built charts — total users, new users over time, active vs. locked-out accounts, email-confirmed vs. pending, 2FA adoption, role distribution; (2) a User Management console evolving the existing read-only grid with search/filter/sort/pagination plus lock/unlock, force 2FA reset, role edit, and soft-delete actions, all via validated DTO-based commands under the existing AdministratorOrSuperUser policy; (3) additive to SPEC-000's existing /api/v1/users endpoints and AdminUsersPage/AdminRoute, not a rewrite; (4) non-goals: no billing/analytics-engine integration, no audit-log UI, no bulk import/export, no new roles."

## Clarifications

### Session 2026-07-28

- Q: Should an Administrator be allowed to lock, force-2FA-reset, or delete their own currently-authenticated account? → A: Hard block — the action is always rejected server-side when the target is the acting admin's own account (no override).
- Q: Who is allowed to grant/revoke the Administrator or Super User role via the role-change action? → A: Only Super User can grant/revoke Administrator or Super User; a plain Administrator can only change a regular user's role.
- Q: What time window/granularity should the new-user registration trend chart use? → A: Daily granularity, trailing 30 days.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Administrator views platform health at a glance (Priority: P1)

An Administrator/Super User logs in, opens the Admin Dashboard, and immediately sees the current state of the user base — how many people are registered, how that's trending over time, how many accounts are locked out or still unconfirmed, how many have two-factor authentication enabled, and how roles are distributed — without having to query the database or export data manually.

**Why this priority**: This is the core value of the feature — visibility into platform health that exists nowhere today. Without it, an administrator has no way to answer basic "how is this application being used/is anything wrong" questions short of a direct database query.

**Independent Test**: Log in as an Administrator/Super User, navigate to the Admin Dashboard, and confirm each metric (total users, new-user trend, active/locked split, confirmed/pending split, 2FA adoption rate, role distribution) is visible and matches the underlying user data.

**Acceptance Scenarios**:

1. **Given** an Administrator/Super User is authenticated, **When** they navigate to the Admin Dashboard, **Then** they see summary metrics and charts reflecting the current user base.
2. **Given** the dashboard is displaying a metric (e.g., total users), **When** the underlying data changes (a new user registers, an account is locked), **Then** the next time the administrator loads or refreshes the dashboard, the metric reflects the change.
3. **Given** an Administrator/Super User on a mobile or narrow viewport, **When** they view the dashboard, **Then** the charts remain legible and usable (responsive layout).
4. **Given** an Administrator/Super User with the light or dark theme active, **When** they view the dashboard, **Then** the charts render legibly and consistently with the active theme.

---

### User Story 2 - Administrator manages a user's account (Priority: P1)

An Administrator/Super User, while reviewing the user list, needs to act on a specific account: lock it (e.g., a compromised or abusive account), unlock a previously locked account, change a user's role, force a user to re-enroll their two-factor authentication, or remove (soft-delete) an account entirely.

**Why this priority**: This restores the core administrative capability the legacy Control Panel provided (account remediation) but which the current migrated screen entirely lacks (it is read-only). Without this, an administrator cannot act on anything they observe.

**Independent Test**: As an Administrator/Super User, select a specific user from the management console and perform each action (lock, unlock, role change, force 2FA reset, soft-delete) one at a time; confirm each action succeeds, is reflected in the list immediately after, and is rejected if attempted by a non-admin session directly against the underlying endpoint.

**Acceptance Scenarios**:

1. **Given** an unlocked user account, **When** an Administrator locks it, **Then** the account is marked locked and that user can no longer authenticate.
2. **Given** a locked user account, **When** an Administrator unlocks it, **Then** the account can authenticate again.
3. **Given** a user with an assigned role, **When** a Super User changes that user's role (including granting or revoking Administrator/Super User), **Then** the user's effective permissions reflect the new role on their next request.
4. **Given** a plain Administrator (not Super User) attempts to grant or revoke the Administrator or Super User role on any account, **When** the request is submitted, **Then** the system rejects it server-side; the Administrator may still change a regular user's role.
5. **Given** a user with two-factor authentication enrolled, **When** an Administrator forces a 2FA reset, **Then** the user's existing enrollment is cleared and they are prompted to re-enroll on next login requiring 2FA.
6. **Given** a user account, **When** an Administrator deletes it, **Then** the account is soft-deleted (no longer able to authenticate or appear in the active user list) without being physically erased from the data store.
7. **Given** any of the above actions, **When** it is attempted before confirming a destructive-action prompt, **Then** the system requires explicit confirmation before applying it.
8. **Given** an update to a user record, **When** the command is processed, **Then** only the explicitly permitted fields are changed — unexpected or extra fields in the request are rejected, not silently persisted.

---

### User Story 3 - Administrator finds a specific user quickly (Priority: P2)

An Administrator/Super User with a growing user base needs to locate a specific user by name or email, sorted or paginated, rather than scanning an unbounded flat list.

**Why this priority**: Improves usability of User Story 2's actions as the user base grows; not blocking for an initial small user base, but necessary for the console to remain usable.

**Independent Test**: As an Administrator/Super User, search the user management console by a partial name or email and confirm only matching results are returned; sort by a column and confirm order changes accordingly; page through results beyond a single page.

**Acceptance Scenarios**:

1. **Given** a list of registered users, **When** an Administrator searches by partial name or email, **Then** only matching users are displayed.
2. **Given** a list of registered users, **When** an Administrator sorts by a column (e.g., email, registration date), **Then** the list reorders accordingly.
3. **Given** more users than fit on one screen, **When** an Administrator scrolls or navigates pagination controls, **Then** additional users load without the page becoming unresponsive.

---

### Edge Cases

- What happens when a non-Administrator/Super User navigates directly to the Admin Dashboard or attempts any management action's underlying endpoint? The system must deny access server-side (not merely hide the UI), consistent with the existing `AdministratorOrSuperUser` policy.
- What happens when an Administrator attempts to lock, delete, or force-2FA-reset their own account? The system MUST reject the action server-side unconditionally — there is no override or warn-and-proceed path (see Clarifications).
- What happens when the user base is empty (fresh deployment) or a metric has no data (e.g., no new users in the selected period)? Charts must render an empty/zero state rather than erroring.
- What happens when two administrators act on the same user account concurrently (e.g., one locks while another edits the role)? The existing concurrency-token convention used elsewhere in the system applies — the second write is rejected/conflict-flagged rather than silently overwriting the first.
- What happens when an Administrator soft-deletes a user who has existing chats or other owned data? The user's data remains subject to the same retention behavior as other soft-deleted entities in the system; this feature does not alter chat retention rules.
- What happens when an action would leave the system with no active Super User? The system MUST reject it (FR-023), regardless of whether the acting admin is targeting themselves or another account.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an Admin Dashboard page, accessible only to Administrator/Super User roles, summarizing current platform user statistics.
- **FR-002**: Dashboard MUST display total registered user count.
- **FR-003**: Dashboard MUST display a trend of new user registrations at daily granularity over the trailing 30 days.
- **FR-004**: Dashboard MUST display the split between active and locked-out user accounts.
- **FR-005**: Dashboard MUST display the split between email-confirmed and pending-confirmation accounts.
- **FR-006**: Dashboard MUST display the two-factor-authentication adoption rate (share of users with 2FA enabled).
- **FR-007**: Dashboard MUST display the distribution of users across existing roles (Administrator/Super User/regular).
- **FR-008**: All dashboard charts MUST render responsively (usable on both desktop and mobile viewports) and remain legible in both the application's light and dark themes.
- **FR-009**: System MUST evolve the existing user management console to support searching users by partial name or email match.
- **FR-010**: System MUST support sorting the user management list by at least one attribute (e.g., email, registration date).
- **FR-011**: System MUST paginate the user management list rather than requiring all users to load and render at once.
- **FR-012**: System MUST allow an Administrator/Super User to lock a user account, preventing that user from authenticating.
- **FR-013**: System MUST allow an Administrator/Super User to unlock a previously locked user account.
- **FR-014**: System MUST allow an Administrator/Super User to change a user's assigned role, EXCEPT that only a Super User may grant or revoke the Administrator or Super User role itself — a plain Administrator may change a regular user's role but MUST be rejected server-side if they attempt to grant or revoke Administrator/Super User.
- **FR-015**: System MUST allow an Administrator/Super User to force-reset a user's two-factor-authentication enrollment, requiring that user to re-enroll.
- **FR-016**: System MUST allow an Administrator/Super User to soft-delete a user account, preventing further authentication while preserving the underlying record per the system's existing soft-delete convention.
- **FR-017**: System MUST require explicit confirmation before applying any destructive action (lock, force 2FA reset, delete).
- **FR-018**: System MUST authorize every dashboard and user-management action — reading dashboard metrics and performing lock/unlock/role-change/2FA-reset/delete — under the existing `AdministratorOrSuperUser` policy, enforced server-side, not only hidden client-side.
- **FR-019**: System MUST process every user-management action as a validated, explicit command accepting only its intended fields — it MUST NOT accept or persist arbitrary/unexpected fields from the request (no mass assignment), consistent with the existing ban on ever exposing or accepting password hash, security stamp, or concurrency stamp values directly.
- **FR-020**: System MUST continue to never return password hash, security stamp, or other Identity secrets in any dashboard or user-management response.
- **FR-021**: System MUST log each administrative action (lock, unlock, role change, 2FA reset, delete) as a security-relevant event, including which administrator performed it and against which target user.
- **FR-022**: System MUST unconditionally reject, server-side, any attempt by an Administrator to lock, force-2FA-reset, or delete their own currently-authenticated account — there is no override or confirmation path that allows this action to proceed against oneself.
- **FR-023**: System MUST reject any role-change, lock, or delete action that would result in zero active (non-locked, non-deleted) Super User accounts remaining in the system — including a Super User attempting to revoke their own Super User role. This closes an operational-lockout risk distinct from FR-022's self-action guard (which only covers self-targeting; this covers the *last remaining* Super User regardless of who targets them).

### Key Entities

- **ApplicationUser** (existing, unchanged shape) — the Identity user account; this feature adds new administrative operations against it (lock/unlock/role-change/2FA-reset/soft-delete) but does not add new persisted fields.
- **Role / UserRole** (existing, unchanged) — reused as-is; no new roles are introduced by this feature.
- **Dashboard Summary** (new, computed/read-only) — an aggregate view over existing user data (counts, trends, rates) assembled for display; not a new persisted entity, since it is derived entirely from existing `ApplicationUser`/`UserRole` data.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An Administrator/Super User can view all six core platform metrics (total users, new-user trend, active/locked split, confirmed/pending split, 2FA adoption, role distribution) from a single dashboard view without navigating to a separate screen or exporting data.
- **SC-002**: An Administrator/Super User can lock, unlock, change the role of, force-2FA-reset, or soft-delete a specific user account in three interactions or fewer (locate user, select action, confirm).
- **SC-003**: A non-Administrator/Super User session is denied 100% of the time when directly requesting any dashboard or user-management action, whether through the UI route or the underlying endpoint.
- **SC-004**: 100% of user-management update requests containing unexpected or extra fields are rejected rather than persisted.
- **SC-005**: An Administrator/Super User can locate a specific user by partial name or email search in under 10 seconds for the current expected scale (fewer than 100 total registered users).
- **SC-006**: Dashboard charts remain legible and usable (no overlapping/clipped content) across both desktop and mobile viewport widths and in both light and dark themes.
- **SC-007**: No dashboard or user-management response ever discloses a password hash, security stamp, or equivalent Identity secret.

## Assumptions

- **Additive to SPEC-000, not a replacement.** The existing `GET /api/v1/users` / `PATCH /api/v1/users/{userId}` endpoints and the `AdminUsersPage`/`AdminRoute` client foundation (delivered under SPEC-000's User Story 4) are reused and extended; this feature only adds the actions, search/sort/pagination, and dashboard summary that do not yet exist.
- **Scale assumption inherited from SPEC-000.** Fewer than 100 total registered users with low concurrency; dashboard aggregate queries and pagination are sized for this scale, not for a large multi-tenant load.
- **Dashboard data freshness.** Metrics are refreshed on page load/manual refresh; live/real-time push updates are not required for this feature.
- **Soft-delete, not physical deletion.** "Delete" a user account follows the same soft-delete convention (audit columns, recoverability) already used elsewhere in the migrated system, rather than physically removing the row.
- **2FA force-reset scope.** Forcing a 2FA reset clears the user's existing TOTP enrollment so they must re-enroll; it does not itself notify the user by email (no new notification capability is introduced by this feature).
- **No new roles or audit-log UI.** Only the existing Administrator/Super User roles are used; a dedicated audit-log viewing screen is explicitly out of scope (per the feature's stated non-goals), even though individual actions are logged (FR-021) for existing operational/security-log tooling to consume.
- **Charting implementation is a stated stakeholder constraint, not left to planning.** The feature description explicitly mandates that dashboard charts be built directly with d3.js rather than a higher-level charting wrapper library; this is recorded here as a binding constraint for the planning phase rather than an open technical decision.
- **Self-action safeguard.** An administrator is assumed to still need some way to manage their own account (e.g., through the existing profile/settings screens); this feature only hard-blocks the *admin-console* actions (lock/2FA-reset/delete) against one's own currently-authenticated account (FR-022, confirmed 2026-07-28), not all self-service account management.
