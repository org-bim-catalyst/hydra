# Feature Specification: Bulk Select-All on Admin List Screens

**Feature Branch**: `056-bulk-select-all`

**Created**: 2026-09-15

**Status**: Draft

**Input**: User description: "In the users, roles, roles permissions page add select all button for non-built-in entities"

## Context

The admin panel's Users screen (specs/001-admin-dashboard), Roles screen, and Role assignments screen (both specs/055-role-management) currently act on one row at a time — an admin locks, deletes, or reassigns a single user, or deletes a single custom role, through that row's own menu. This feature adds a per-screen "select all" affordance so an admin can select every eligible row at once, then apply one bulk action to all of them together, instead of repeating the same action row by row.

"Non-built-in entities" means select-all must never pull in a row the admin isn't allowed to bulk-act on in the first place: the built-in Administrator/Super User roles on the Roles screen, and users holding either of those roles or currently locked, on the other two screens.

## Clarifications

### Session 2026-09-15

- Q: What bulk action does "select all" enable on the Users screen? → A: Three separate buttons — Lock/Unlock, Force 2FA reset, Delete — each enabled only while one or more rows are selected, disabled otherwise.
- Q: What bulk action does "select all" enable on the Roles screen? → A: One button — delete all selected roles.
- Q: Does "select all" mean every eligible row platform-wide, or only the eligible rows on the current page? → A: "Select all" checks the eligible rows on the current page. When a bulk action is then triggered, a confirmation dialog states both counts — how many rows are selected on this page, and how many eligible rows match across every page of the current filter/search — and lets the admin choose to act on the page's selection or expand to the full matching set.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Select all eligible users for a bulk action (Priority: P1)

An Administrator or Super User on the Users screen ticks "select all" to select every listed user except ones the action wouldn't apply to (self, locked, or holding a privileged role, depending on the action), reviews the count, and applies one bulk action to the whole selection in a single confirmation instead of repeating it per user.

**Why this priority**: Users is the screen an admin manages most often and where repetitive one-row-at-a-time actions cost the most time.

**Independent Test**: With 20 regular users listed, an Administrator, and one locked user in the list, click "select all," confirm the locked user and the admin's own row are not selected, then run the bulk action and confirm it applied to every selected row and to no others.

**Acceptance Scenarios**:

1. **Given** the Users screen lists a mix of regular users, locked users, and privileged-role holders, **When** an admin clicks "select all," **Then** only rows a bulk action can legally apply to are selected on this page, and the count of selected rows is visible.
2. **Given** some rows are selected via "select all," **When** the admin clicks it again (or "select none"), **Then** the selection clears.
3. **Given** a full selection, **When** the admin manually deselects one row, **Then** "select all" reflects that the selection is now partial (not fully checked).
4. **Given** one or more rows selected, **When** the admin views the toolbar, **Then** the Lock/Unlock, Force 2FA reset, and Delete buttons are enabled; **When** nothing is selected, **Then** all three are disabled.
5. **Given** a selection where every selected user is currently locked, **When** the admin views the Lock/Unlock button, **Then** it reads "Unlock selected"; **given** a selection with at least one unlocked user, **when** the admin views it, **then** it reads "Lock selected" and locking skips any already-locked row in the selection (reported, not silently ignored).
6. **Given** a selection spanning only the current page, **When** the admin triggers any of the three bulk actions, **Then** a confirmation shows both the number selected on this page and the total number of eligible rows matching the current filter/search across all pages, and lets the admin choose which scope to act on.
7. **Given** a confirmed bulk action, **When** it completes, **Then** each affected user is acted on, and any that fail are reported individually with a reason — nothing fails silently.

---

### User Story 2 - Select all custom roles for bulk deletion (Priority: P2)

An Administrator or Super User on the Roles screen ticks "select all" to select every custom role (built-in roles are never selectable), reviews the count and how many users would be left with no role, and deletes them all in one confirmation.

**Why this priority**: Deleting several unused custom roles one at a time is the main repetitive task on this screen; it's lower priority than Users because Roles is used less often.

**Independent Test**: With 5 custom roles and the 2 built-in roles listed, click "select all," confirm exactly 5 are selected, delete them, and confirm all 5 are gone and the built-in roles remain untouched.

**Acceptance Scenarios**:

1. **Given** the Roles screen lists built-in and custom roles, **When** an admin clicks "select all," **Then** only custom roles on this page are selected — built-in rows have no selection control at all.
2. **Given** one or more custom roles selected, **When** the admin views the toolbar, **Then** a single "Delete selected" button is enabled; **when** nothing is selected, **then** it is disabled.
3. **Given** a selection spanning only the current page, **When** the admin clicks "Delete selected," **Then** the confirmation states both how many roles are selected on this page and how many custom roles match the current filter/search across all pages, lets the admin choose which scope to delete, and states the total number of users across the roles being deleted who will be left with no role.
4. **Given** the admin confirms, **Then** every role in the chosen scope is deleted and those users lose the role — built-in roles are never affected regardless of scope.

---

### User Story 3 - Select all eligible users for bulk role assignment (Priority: P1)

An Administrator or Super User on the Role assignments screen picks a role, ticks "select all" to select every user that role can legally be assigned to (excluding locked users and, unless acting as Super User, users holding a privileged role), and assigns the role to the whole selection in one action.

**Why this priority**: This is the bulk-assignment flow specs/055-role-management already scoped (its User Story 4) — this feature adds the "select all" affordance that flow was missing, so it shares that story's priority.

**Independent Test**: With 20 assignable users and 2 privileged-role holders listed, select a role, click "select all," confirm the 2 privileged-role holders are excluded, assign, and confirm the role landed on exactly the 20 eligible users.

**Acceptance Scenarios**:

1. **Given** the Role assignments screen with a role selected, **When** an admin clicks "select all," **Then** every eligible user on this page is selected, and a user the acting admin isn't allowed to change (locked, or privileged-role-holding for a plain Administrator) is excluded from the selection entirely.
2. **Given** a selection spanning only the current page, **When** the admin triggers the bulk-assign action, **Then** the confirmation states both how many are selected on this page and how many eligible users match the current filter/search across all pages, and lets the admin choose which scope to assign.
3. **Given** the admin confirms, **When** the assignment runs, **Then** the result reports how many succeeded, matching the confirmed scope's count exactly (no silent skips, since ineligible rows were never selectable).

---

### Edge Cases

- Zero eligible rows are listed (e.g., every role is built-in, or every user is locked) — "select all" is disabled or clearly shows there is nothing to select, not silently a no-op.
- An admin selects all on the current page, then changes the sort, filter/search, or page — the page-scoped selection is cleared rather than silently carried over to rows the admin never saw selected.
- An admin picks the "all matching pages" scope in the confirmation, and the true matching count changes between when the dialog opened and when it's confirmed (another admin changed something) — the action still reports its actual per-row results afterward rather than assuming the earlier count held.
- Two admins act at the same time; one bulk-deletes a role the other just selected — the confirmation/apply step reports that specific row as no longer available rather than failing the whole batch opaquely.
- A plain Administrator uses "select all" on Role assignments where some listed users hold a privileged role — those rows are excluded from selection up front, not selected-then-rejected at apply time.
- A mixed Users selection (some locked, some not) is bulk-acted on with "Lock/Unlock" — the action applies per-row (locks the unlocked ones, leaves/reports the already-locked ones as skipped) rather than failing the whole batch or guessing a single intent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Each of the Users, Roles, and Role assignments screens MUST offer a "select all" control that checks every row on the current page that is eligible for that screen's bulk action(s), and MUST support clearing the selection (toggle or "select none").
- **FR-002**: The Users screen MUST offer three bulk actions — **Lock/Unlock**, **Force 2FA reset**, and **Delete** — each its own button, each enabled only while one or more rows are selected and disabled otherwise. Eligibility for selection excludes only the acting admin's own row and (for Lock and Delete specifically) a row that would strand the system with zero active Super Users. **Correction (found during planning, research.md Decision 1)**: no existing rule stops one admin from locking, unlocking, force-2FA-resetting, or deleting another Administrator's or Super User's account today — that protection exists only for role changes (specs/055-role-management FR-016), not for these four actions. Bulk eligibility matches that: it does **not** additionally exclude a privileged-role-holding row, so bulk behaves identically to the existing single-row menu (FR-010) rather than being stricter for no reason. The **Lock/Unlock** button's action and label adapt to the selection: it reads "Unlock selected" only when every selected user is currently locked, otherwise it reads "Lock selected"; locking skips any already-locked row in the selection and reports it as skipped rather than erroring.
- **FR-003**: The Roles screen MUST offer one bulk action — **Delete selected** — enabled only while one or more custom roles are selected and disabled otherwise. Built-in roles MUST NOT have a selection control at all.
- **FR-004**: The Role assignments screen MUST select only users the currently-selected role can legally be assigned to under the existing privileged-role and locked-user rules (specs/055-role-management FR-016/FR-020). **Correction (found during planning, research.md finding F2)**: specs/055-role-management scoped a bulk-assignment action (its User Story 4) but never built its command, endpoint, or UI — only the underlying repository method exists. This feature builds that action from scratch, alongside its "select all" affordance, rather than adding "select all" to something already shipped.
- **FR-005**: The UI MUST show how many rows are currently selected at all times "select all" or manual selection is active.
- **FR-006**: Manually deselecting one row out of a full "select all" selection MUST visibly change the "select all" control to a partial/indeterminate state, not remain fully checked.
- **FR-007**: "Select all" MUST check only the eligible rows on the currently displayed page. Triggering any bulk action from a page-scoped selection MUST show a confirmation naming two counts — the number selected on this page, and the total number of eligible rows matching the current filter/search across every page — and MUST let the admin choose to act on the page's selection or expand to the full matching set before proceeding.
- **FR-008**: When zero rows are eligible for selection, the "select all" control MUST be disabled or otherwise clearly convey there is nothing to select, rather than silently doing nothing when clicked.
- **FR-009**: Every bulk action MUST report a per-row outcome (succeeded/skipped-with-reason) — consistent with the existing bulk-assignment result reporting in specs/055-role-management FR-019 — and MUST NOT fail or succeed silently, regardless of which scope (page or all-matching) was chosen.
- **FR-010**: Every existing authorization and safeguard rule (privileged-role restriction, last-Super-User safeguard, locked-user exclusion) MUST apply identically whether a row was selected individually, via page-scoped "select all," or via the all-matching-pages scope — bulk selection MUST NOT bypass any rule the single-row action already enforces.

### Key Entities

- **Selection**: A transient, client-side set of row identifiers checked on the current page of one screen; not persisted, cleared on navigating away, changing the sort/filter/search, or changing page.
- **Bulk Action Scope**: The admin's choice, made in the confirmation dialog, between acting on just the page-scoped selection or expanding to every eligible row matching the current filter/search across all pages.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can select every eligible row on a page of 20 and apply one bulk action in 3 interactions or fewer (select all, click the action button, confirm the page-only scope), down from one interaction per row today.
- **SC-002**: An admin can apply a bulk action to every eligible row across a filtered result of 500 spanning many pages in 3 interactions or fewer (select all, click the action button, confirm the all-matching-pages scope), never needing to visit more than one page.
- **SC-003a**: 0 built-in roles are ever included by "select all" on the Roles screen, and 0 locked users or (for a plain Administrator) privileged-role-holding users are ever included by "select all" on the Role assignments screen — verified by automated tests.
- **SC-003b**: 0 self-rows are ever included by "select all" on the Users screen — verified by automated tests. (Per FR-002's correction, a privileged-role-holding user is *not* excluded here, matching the existing single-row Lock/Unlock/Force-2FA-reset/Delete actions — this is intentional, not a gap.)
- **SC-004**: 100% of bulk actions produce a visible per-row result — no action taken through this feature is ever silent about its outcome.

## Assumptions

- This feature adds bulk selection and the three named actions to the Users screen (previously single-row only), one bulk delete action to the Roles screen, and both the bulk-assignment action itself and its "select all" convenience to the Role assignments screen (specs/055-role-management User Story 4 was specified but never built — see FR-004's correction) — it does not introduce any other new action.
- "Matching the current filter/search across every page," for the all-matching-pages scope, means the same search/filter criteria already applied to the visible page, evaluated against the full result set server-side — not merely every row currently loaded in the browser.
- Built-in-role protection, the last-Super-User safeguard, and locked-user exclusion are unchanged — this feature only decides which rows are offered to the selection, never which rules apply once selected.
- Force 2FA reset has no meaningful "already done" state to skip the way Lock/Unlock does — running it again on an already-reset user is harmless and always counts as succeeded.
