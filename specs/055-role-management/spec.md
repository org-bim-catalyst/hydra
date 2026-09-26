# Feature Specification: Role Definition & Role Assignment Screens

**Feature Branch**: `055-role-management`

**Created**: 2026-09-14

**Status**: Draft

**Input**: User description: "In the admin panel add two separate screens: one for roles definition and another for roles assignment, so that super user and admin can define new roles and assign them to users."

## Context

Today the platform recognises exactly two privileged roles — **Administrator** and **Super User** — plus an implicit "Regular" state meaning "no privileged role". These roles are fixed, cannot be created or edited from the product, and are changed per user through a "Change role" action on the Users screen (specs/001-admin-dashboard FR-014).

This feature makes **roles** a manageable concept: application-administration roles such as Super User, Administrator, Moderator, and Viewer, each defining what its holder may see and do in the admin panel through a set of **permissions**. It adds three dedicated admin-panel screens:

- **Permissions** — the catalogue of every permission the platform enforces, and which roles include each one.
- **Roles** — define and maintain roles, including which permissions belong to each role.
- **Role assignments** — decide which user holds which role.

Roles are deliberately distinct from **personas** (e.g., BIM Manager, BIM Coordinator, Project Manager, Architect, Urban Designer, Engineer). Personas describe a professional identity, may later be assigned to users *or* agents, and a user may hold several. Personas are out of scope for this feature and will be specified separately.

## Clarifications

### Session 2026-09-14

- Q: What kind of thing is a role, versus a job title like "BIM Manager"? → A: Roles are application-administration roles (Super User, Administrator, Moderator, Viewer, …). Job/discipline identities (BIM Manager, Architect, Engineer, …) are **personas** — a separate, future concept assignable to users or agents. This feature covers roles only.
- Q: Can a user hold more than one role? → A: No. A user holds at most one role. (A user may later hold one role plus several personas, e.g., Administrator + BIM Manager.)
- Q: What does holding a role grant? → A: Access to admin-panel areas. Each role is a set of permissions chosen from the permission catalogue (see FR-004). *(Inferred from "roles for app administration like moderator, viewer" — confirm or refine via `/speckit-clarify`.)*
- Q: Where are permissions managed? → A: A separate **Permissions** screen presents the permission catalogue; the **Roles** screen is where permissions are attached to or detached from a role.
- Q: Can admins create new permissions on the Permissions screen? → A: No. A permission only has meaning if the platform enforces it, so the catalogue is defined by the platform and grows as features ship. The Permissions screen browses the catalogue and shows role coverage; it does not create, rename, or delete permissions. *(Design decision — an admin-created permission would be checked by nothing and silently grant nothing.)*

### Session 2026-09-26

- Q: What happens to a user whose role is deleted, or who has never been given one? → A: They hold the built-in **User** role. "No role" is retired: every account is assigned User on registration or first external sign-in, deleting a role moves its holders to User, and a roleless legacy account is treated as User.
- Q: Can the User role be changed? → A: It can't be renamed or deleted. Its permissions can only be added to — the basic permissions stay — and it can never carry View user content, since every account holds it.
- Q: Can a role be copied? → A: Yes. A Super User can duplicate any role, built-in included, save it under a new name, and edit the copy as a custom role.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Define and maintain roles (Priority: P1)

An Administrator or Super User opens the **Roles** screen and sees every role — the built-in roles and any custom roles — with each role's name, description, built-in indicator, permission summary, and how many users hold it. They create a new role (e.g., "Moderator") by giving it a unique name, an optional description, and selecting which permissions from the catalogue belong to it (grouped by admin-panel area). They can later edit a custom role's name, description, and permissions, or delete it.

**Why this priority**: A role must exist before it can be assigned; without this screen nothing else in the feature is possible.

**Independent Test**: Sign in as an Administrator, open Roles, create "Viewer" with the "View dashboard" and "View users" permissions, edit it to add "View AI providers", then delete it — confirming each change is reflected immediately and survives a page reload.

**Acceptance Scenarios**:

1. **Given** an Administrator on the Roles screen, **When** they create a role with an unused name and at least one permission, **Then** the role appears in the list with its permission summary and zero assigned users.
2. **Given** an existing role named "Moderator", **When** an admin tries to create or rename another role to "moderator" (any casing, surrounding spaces ignored), **Then** the system rejects it with an inline message that the name is taken.
3. **Given** the built-in Super User and Administrator roles, **When** an admin views them, **Then** they are marked built-in, show their fixed permissions read-only, and offer no rename, permission-edit, or delete action; direct attempts are rejected server-side.
4. **Given** a custom role held by no users, **When** an admin deletes it and confirms, **Then** it disappears from the Roles screen and every role picker.
5. **Given** a custom role held by one or more users, **When** an admin deletes it, **Then** the confirmation states how many users will be moved to the built-in **User** role, and on confirming those users hold the User role and the deleted role is gone.
6. **Given** a role's permissions are edited, **When** a user holding that role makes their next request, **Then** their admin-panel access reflects the new permissions.

---

### User Story 2 - Assign a role to a user (Priority: P1)

An Administrator or Super User opens the **Role assignments** screen, finds a user by name or email, sees the role the user currently holds (every account holds one — at least the built-in **User** role), and changes it by assigning a different role. Returning a user to the User role is how a privileged role is taken away. They can also pick a role and see every user who holds it.

**Why this priority**: Defining roles only delivers value once they can be given to people; together with Story 1 this is the MVP.

**Independent Test**: With a custom role "Viewer" defined, open Role assignments, search for a user on the User role, assign "Viewer", sign in as that user and confirm they can open the permitted admin areas in read-only form and nothing else; then assign the User role back and confirm the admin panel is no longer reachable for them.

**Acceptance Scenarios**:

1. **Given** a user on the User role and a custom role "Viewer", **When** an Administrator assigns "Viewer", **Then** the assignment shows immediately and takes effect on the user's next request.
2. **Given** a user holding "Viewer", **When** an Administrator assigns "Moderator", **Then** "Moderator" replaces "Viewer" — the user never holds two roles.
3. **Given** a user holding "Moderator", **When** an Administrator assigns the User role, **Then** the user holds the User role and loses all admin-panel access the User role does not grant.
4. **Given** a plain Administrator (not Super User), **When** they try to assign or remove the Administrator or Super User role, or change the role of a user who currently holds either, **Then** the screen does not offer the action and a direct request is rejected server-side.
5. **Given** the only remaining active Super User, **When** anyone tries to change or remove that user's role, **Then** the system rejects it, explaining the last Super User cannot be removed.
6. **Given** an admin selects the role "Viewer", **When** the list loads, **Then** it shows every user holding that role, paginated and searchable.

---

### User Story 3 - Browse the permission catalogue (Priority: P2)

An Administrator or Super User opens the **Permissions** screen and sees every permission the platform enforces, grouped by admin-panel area, each with a plain-language description of what it allows and the list of roles that currently include it. They filter by area or by role, and answer questions like "who can manage MCP servers?" by following a permission to its roles and from a role to the Roles screen.

**Why this priority**: Admins need to understand what each permission does before attaching it to roles, and need a permission-centred view for access reviews. Roles can still be defined without it (the Roles screen lists the same permissions), so it follows the P1 stories.

**Independent Test**: With "Moderator" holding "Manage MCP servers", open Permissions, filter to the MCP servers area, confirm "Manage MCP servers" lists Super User, Administrator, and Moderator, then open Moderator from that list and land on its Roles-screen entry.

**Acceptance Scenarios**:

1. **Given** an Administrator on the Permissions screen, **When** it loads, **Then** every catalogue permission is shown grouped by area with its name, description, and the roles that include it.
2. **Given** a permission that no custom role includes, **When** it is viewed, **Then** it shows only the built-in roles.
3. **Given** an admin filters by the role "Viewer", **When** the list updates, **Then** only permissions included in "Viewer" are shown.
4. **Given** an admin on the Permissions screen, **When** they look for a way to create, rename, or delete a permission, **Then** none is offered, and a direct attempt is rejected server-side.
5. **Given** an admin selects a role shown against a permission, **When** they follow it, **Then** they land on that role on the Roles screen, where its permissions can be edited.

---

### User Story 4 - Assign a role to several users at once (Priority: P3)

An admin selects a role and assigns it to several users in one action (e.g., making a whole support team "Moderator"), instead of repeating the change user by user.

**Why this priority**: Saves time when onboarding a team, but Story 2 already covers the need.

**Independent Test**: Select "Moderator", choose five users, assign in one action, and confirm all five now hold "Moderator" and none still holds their previous role.

**Acceptance Scenarios**:

1. **Given** a role and a selection of users, **When** the admin assigns the role in one action, **Then** each selected user's role is replaced with it, and a single summary reports how many succeeded.
2. **Given** the selection includes users the acting admin may not change (e.g., a plain Administrator selecting a Super User), **When** the action runs, **Then** permitted changes are applied, disallowed ones are skipped, and the summary lists each skipped user with the reason — nothing is silently dropped.

---

### Edge Cases

- Two admins edit the same role, or change the same user's role, at the same time — the later conflicting write is rejected with a "changed by someone else, reload" message rather than silently overwriting.
- A role is deleted while another admin is assigning it — the assignment fails with a clear "role no longer exists" message.
- A role name that is empty, whitespace-only, too long, or equal to a built-in role name or "Regular"/"No role" is rejected with an inline message.
- A custom role saved with no permissions at all is rejected — a role must grant at least one permission.
- A user whose role grants no permission for a given admin area navigates to it directly — access is denied server-side and the area is absent from their navigation.
- A signed-in user's role is changed, edited, or removed — the change applies on their next request, matching existing role-change behaviour.
- An admin changes or removes their own role — governed by the existing self-action and last-Super-User safeguards (specs/001-admin-dashboard FR-022/FR-023).
- The target user is locked or soft-deleted — they do not appear as assignment targets.
- The Users screen's existing "Change role" action must show the same role data as the new screens.
- Any failed load, save, delete, or assignment on either screen shows a visible error with a retry path; nothing fails silently.

## Requirements *(mandatory)*

### Functional Requirements

**Access to the three screens**

- **FR-001**: System MUST add three separate admin-panel screens — **Permissions**, **Roles**, and **Role assignments** — reachable from the admin panel navigation.
- **FR-002**: The Permissions, Roles, and Role assignments screens, and every read and write behind them, MUST be restricted server-side to users holding the built-in Administrator or Super User role. This access MUST NOT be grantable through a custom role's permissions, and no catalogue permission may cover these screens.

**Permission catalogue**

- **FR-027**: The permission catalogue MUST be defined by the platform, not by admins: every permission in it corresponds to access the platform actually enforces, and it grows only when a feature that enforces a new permission ships.
- **FR-028**: Each permission MUST have a stable identifier, a display name, the admin-panel area it belongs to, and a plain-language description of what it allows.
- **FR-029**: The Permissions screen MUST list every catalogue permission grouped by area, showing its display name, description, and the roles (built-in and custom) that include it.
- **FR-030**: The Permissions screen MUST support filtering by area and by role, and each role shown MUST link to that role on the Roles screen.
- **FR-031**: The Permissions screen MUST NOT offer creating, renaming, editing, or deleting permissions, nor changing which roles include a permission; role-permission membership is edited only on the Roles screen. Direct attempts to create, change, or delete a permission MUST be rejected server-side.

**Role definition**

- **FR-003**: Administrators and Super Users MUST be able to create a custom role with a name (required, 2–50 characters, unique case-insensitively after trimming), an optional description (up to 250 characters), and a set of permissions.
- **FR-004**: The Roles screen MUST be where permissions are attached to and detached from a role, by selecting from the permission catalogue grouped by area. The initial catalogue MUST contain a *View* and a *Manage* permission for each admin-panel area — Users, AI providers, Default models, AI capabilities, Agent policies, Workflow policies, MCP servers — and a *View* permission only for the read-only areas Dashboard and System agents, where *View* allows read-only access and *Manage* allows changes. Selecting a *Manage* permission MUST also include the matching *View* permission. A custom role MUST include at least one permission.
- **FR-005**: A user MUST be able to see in the admin panel navigation, open, and act within only the areas their role grants, at the granted level; every request outside that grant MUST be rejected server-side.
- **FR-006**: Administrators and Super Users MUST be able to edit a custom role's name, description, and permissions, subject to FR-003/FR-004 validation; changes apply to all holders on their next request.
- **FR-007**: Administrators and Super Users MUST be able to delete a custom role; when it is held by any user, the confirmation MUST state how many users will be moved to the User role, and deletion MUST move those users to the User role — never leave them with no role.
- **FR-008**: The built-in **Super User** and **Administrator** roles MUST include every catalogue permission, including permissions added to the catalogue in future. Both MUST be listed on the Roles screen, marked built-in, and MUST NOT be renamed, have their permissions changed, or be deleted; attempts MUST be rejected server-side.
- **FR-009**: Built-in roles MUST keep every privilege they have today that lies outside the catalogue (e.g., background-job and diagnostics access, rate-limit treatment); this feature MUST NOT reduce what existing Administrators and Super Users can do.
- **FR-010**: The Roles screen MUST show, per role: name, description, built-in/custom indicator, a permission summary, number of users holding it, and last-modified date.
- **FR-011**: When a permission is added to the catalogue in future, it MUST NOT be included in any existing custom role until an admin attaches it; when a permission is retired from the catalogue, it MUST be removed from every role and the removal recorded in the audit trail.

**Role assignment**

- **FR-012**: A user MUST hold exactly one role. Assigning a role to a user who already holds one MUST replace it. There is no "no role" state: the built-in **User** role (formerly the implicit "Regular" state) is every account's floor.
- **FR-012a**: Every new account — self-registration and first external (OAuth) sign-in alike — MUST be given the User role when it is created; if that fails, the account MUST NOT be left behind without it.
- **FR-012b**: The User role is built-in and MUST NOT be renamed or deleted. Its description and permissions MAY be edited, but only by adding: its basic permissions (the minimum needed to use the site) MUST NOT be removable, and it MUST NOT be given a permission only a Super User controls (View user content). It grants no administrative privilege by default.
- **FR-012c**: A Super User MUST be able to duplicate any role — built-in included — into a new custom role with a new name and the source role's permissions. The copy holds no users. A plain Administrator's attempt MUST be rejected server-side.
- **FR-013**: The Role assignments screen MUST let an admin search users by name or email and see each user's current role; an account with no stored role row (legacy data) MUST be shown and treated as holding the User role.
- **FR-014**: The Role assignments screen MUST let an admin select a role and view the paginated, searchable list of users holding it.
- **FR-015**: Administrators and Super Users MUST be able to assign, change, or remove a user's role for any active user, subject to FR-016 and FR-017.
- **FR-016**: Only a Super User MAY assign or remove the Administrator or Super User role, or change the role of a user who currently holds either; a plain Administrator's attempt MUST be rejected server-side (preserves specs/001-admin-dashboard FR-014).
- **FR-017**: System MUST reject any role change, role removal, or role deletion that would leave zero active Super Users (preserves specs/001-admin-dashboard FR-023).
- **FR-018**: Role changes MUST take effect for the affected user no later than their next request.
- **FR-019**: Administrators and Super Users MUST be able to assign one role to multiple users in a single action, receiving a per-user result summary that lists any skipped users and why.
- **FR-020**: Locked and soft-deleted users MUST NOT be offered as assignment targets.
- **FR-021**: The existing "Change role" action on the Users screen MUST reflect the same role data as the new screens and offer every role (built-in and custom) under the same rules; it MAY instead link to the Role assignments screen.

**Integrity, audit & feedback**

- **FR-022**: Concurrent conflicting edits to the same role, or to the same user's role, MUST be detected and the later write rejected with a reload prompt, never silently overwritten.
- **FR-023**: System MUST record every role create, edit (including before/after permissions), delete, assignment, change, and removal as a security audit event capturing the acting admin, the target role, the target user (where applicable), before/after values, and the time.
- **FR-024**: Every access denial caused by a role's permissions (FR-005) MUST be recorded as an authorization-denial audit event.
- **FR-025**: Every failed load, create, edit, delete, or assignment on either screen MUST produce visible feedback (inline error or notification) with a way to retry.
- **FR-026**: All three screens MUST support light and dark themes, be keyboard operable, and remain usable at mobile widths.

### Key Entities

- **Role**: An application-administration role. Attributes: name (unique, case-insensitive), description, built-in flag, permission set, created/modified audit stamps. Built-in roles (Super User, Administrator) are protected; custom roles are created by admins.
- **Permission**: A platform-defined, enforced capability in the catalogue. Attributes: stable identifier, display name, admin-panel area, description. Not created or edited by admins.
- **Role Permission**: The link between a role and a permission it includes; edited only from the Roles screen.
- **Role Assignment**: The link between a user and the single role they hold. Attributes: user, role, when assigned, assigned by.
- **Role Audit Event**: An immutable record of a role definition or assignment change — actor, action, target role, target user (if any), before/after values, timestamp.
- **User** (existing): The account that holds zero or one role.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An admin can create a new custom role with its permissions in under 2 minutes, starting from anywhere in the admin panel.
- **SC-002**: An admin can assign a role to a specific user in 4 interactions or fewer (open screen, find user, pick role, confirm).
- **SC-003**: An admin can assign a role to 20 users in a single action in under 2 minutes.
- **SC-004**: 100% of role-definition and role-assignment changes appear in the security audit trail with actor, target, and before/after values.
- **SC-005**: 0 successful unauthorized actions in automated tests: every request by a custom-role holder outside their granted areas/levels, by a non-admin against the two screens, and by a plain Administrator against a privileged role is rejected server-side.
- **SC-006**: The system never reaches a state with zero active Super Users through any action on these screens.
- **SC-007**: Existing Administrators and Super Users retain 100% of their current capabilities after release (verified by the existing admin test suites passing unchanged).
- **SC-008**: Permission, role, and assignment lists load and respond to search within 2 seconds for an organisation of 10,000 users and 200 roles.
- **SC-009**: An admin can answer "which roles can manage a given admin area?" from the Permissions screen in 3 interactions or fewer.
- **SC-010**: 100% of catalogue permissions are enforced: automated tests confirm that, for every permission, a role without it is denied and a role with it is allowed.

## Assumptions

- "Super user and admin" refers to the existing built-in **Super User** and **Administrator** roles; only they can use the three new screens, with the existing privileged-role restriction (FR-016) and last-Super-User safeguard (FR-017) carried forward unchanged.
- Moderator, Viewer, and similar roles are examples of what admins will create through the Roles screen; they are not pre-seeded.
- Custom roles grant admin-panel access only; they do not change what a user can do in the regular (non-admin) workspace, and they cannot grant access to the Permissions, Roles, or Role assignments screens (FR-002).
- The platform is single-organisation today; roles are global to the deployment, not per-tenant.
- Existing users keep their current role; users with no privileged role are given the User role by the 2026-09-26 migration (research.md Decision 12).
- Deleting a role removes its definition and assignments; the audit trail (FR-023) preserves the history.
- Pagination follows the existing offset-based convention for small admin lists.
- A screen for browsing the role audit trail is out of scope; events go to the existing security audit trail.
- **Out of scope**: personas (BIM Manager, BIM Coordinator, Project Manager, Architect, Urban Designer, Engineer, …) and their assignment to users or agents — a future feature. The role model must not prevent a user from later holding one role plus multiple personas.
- Also out of scope: per-tenant roles, role hierarchies/inheritance, time-limited assignments, admin-defined custom permissions, and self-service role requests. The initial catalogue stops at View/Manage per area; finer per-action permissions can be added to the catalogue by later features without changing this model.
