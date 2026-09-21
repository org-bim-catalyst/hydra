# Feature Specification: Admin Panel Layout & Polish Pass

**Feature Branch**: `062-admin-panel-layout-polish`

**Created**: 2026-09-20

**Status**: Implemented (US1-US5 shipped; see [Post-implementation addendum](#post-implementation-addendum) below for follow-on polish requested during review that extends past this spec's original scope)

**Input**: User description: "Admin panel layout & polish pass: 1. AI Providers page: split the 'Possibly out of date' staleness chip out of the Health column into its own separate column (it currently wraps inline with the status chip and its tooltip overlaps the row below). 2. Default models page (and this pattern applies to ALL admin panel pages): stretch the sidebar and the tab content to fill the full viewport height, matching what was done on the Account Settings page. 3. On any admin page that has an info/hint banner (e.g. Default models, AI Capabilities), push that hint to the bottom of the tab content area (adjust paddings/margins) instead of the top. 4. Workflow Policies and Agent Policies pages: convert the 'New Policy' inline creation section into a modal opened from a button (same pattern as MCP servers page), so the policies table can occupy the full page height and show more information/columns. 5. Admin sidebar: move 'System Agents' to right after 'Role assignments', and add a horizontal divider separating that group from the rest of the nav items."

## Clarifications

### Session 2026-09-20

- Q: When an admin page's table/content is shorter than the viewport, how should the hint banner be positioned relative to the true bottom of the page? → A: The table/content area itself always stretches to occupy the full height of the tab content area (absorbing any extra space), so the hint sits immediately under the table's bottom edge and just above the page's bottom edge — with no gap in between — on every page, regardless of how few rows the table has.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Readable provider health at a glance (Priority: P1)

An administrator opens the AI Providers page to check whether providers are healthy. Today the staleness warning chip wraps inline next to the status chip, its tooltip overlaps the row beneath it, and the two signals ("is it healthy" and "is that verdict recent") blur together. The admin needs to read both signals cleanly without visual overlap.

**Why this priority**: This is an active display bug (overlapping tooltip, wrapped chips) affecting a page admins check regularly to diagnose provider incidents. It's the smallest, most self-contained fix and unblocks correct reading of provider state.

**Independent Test**: Open AI Providers with a provider whose health result is stale. Confirm the staleness indicator renders in its own column, never wraps into the status chip, and its tooltip never overlaps an adjacent row.

**Acceptance Scenarios**:

1. **Given** a provider whose health check has not run recently, **When** the admin views the AI Providers table, **Then** the staleness indicator appears in a dedicated column separate from the health status chip.
2. **Given** a provider with a stale health result, **When** the admin hovers the staleness indicator, **Then** the tooltip displays without overlapping any other row's content.
3. **Given** a provider whose health result is current (not stale), **When** the admin views the table, **Then** the staleness column shows no indicator for that row.

---

### User Story 2 - Full-height admin pages (Priority: P1)

An administrator navigates between admin pages (Default models, AI Capabilities, Users, Roles, etc.). Today the sidebar and tab content stop short of the viewport height, leaving dead space and making the layout feel inconsistent with the Account Settings page, which already fills the viewport. The admin wants every admin page to consistently use the full available height.

**Why this priority**: This is a global layout inconsistency affecting every admin page, and it's a prerequisite for User Story 4 (fuller policy tables) to actually gain usable space.

**Independent Test**: Open any admin panel page at various viewport heights (including a tall monitor) and confirm the sidebar and the active tab's content both extend to fill the full available height with no dead space or premature cutoff, matching the Account Settings page's behavior.

**Acceptance Scenarios**:

1. **Given** the admin panel is open on any page, **When** the viewport height changes, **Then** the sidebar and tab content resize to fill the full available height.
2. **Given** a tab's content is shorter than the viewport, **When** the admin views the page, **Then** the primary content's table/list container itself stretches to absorb the remaining height, rather than leaving dead space below a fixed-size table.
3. **Given** a tab's content is taller than the viewport, **When** the admin views the page, **Then** the content area scrolls independently while the sidebar remains fully visible, consistent with the Account Settings page.

---

### User Story 3 - Hint banners anchored to the bottom (Priority: P2)

An administrator opens a page like Default models or AI Capabilities that shows an informational hint banner. Today the hint sits at the top, pushing the actual working content (the table/list the admin came to use) down. The admin wants the hint out of the way, positioned directly under the table's bottom edge and just above the page's bottom edge, so the working content is immediately visible and the hint reads as a footnote rather than a leading blocker.

**Why this priority**: Improves usability once the full-height layout (User Story 2) is in place, but is a smaller visual refinement than the layout and modal changes.

**Independent Test**: Open Default models and AI Capabilities (both pages with a hint banner, and on Default models specifically a table short enough to leave spare height). Confirm the primary table/content renders at the top and stretches to fill the available height, and the hint banner sits directly beneath the table's bottom edge with no gap, ending just above the page's bottom edge — on every page, not only ones whose table happens to be tall enough to reach the bottom on its own.

**Acceptance Scenarios**:

1. **Given** an admin page has an informational hint banner, **When** the admin opens that page, **Then** the primary content (table/list) renders at the top of the tab content area, stretches to fill the available height, and the hint banner renders immediately below it with no gap.
2. **Given** the table has few rows and would otherwise leave spare vertical space, **When** the admin views the page, **Then** the table's container still stretches to occupy that spare space so the hint sits just above the page's bottom edge rather than directly under a short table with a visible gap beneath the hint.
3. **Given** the tab content is taller than its visible area, **When** the admin scrolls, **Then** the hint banner remains the last element below the table rather than floating mid-content.
4. **Given** an admin page has no hint banner, **When** the admin opens that page, **Then** its layout is unaffected by this change.

---

### User Story 4 - Policy creation moved into a modal (Priority: P2)

An administrator managing Workflow Policies or Agent Policies wants to see as many existing policies as possible without scrolling past an inline "New Policy" creation form first. Today that creation form is permanently embedded in the page, competing for space with the policies table. The admin wants to create a new policy via a button that opens a modal (matching the MCP servers page pattern), freeing the full page height for the table.

**Why this priority**: Delivers real information-density value to a workflow the admin uses often, but depends on no other story and can ship after the higher-priority layout/health fixes.

**Independent Test**: Open Workflow Policies (and separately, Agent Policies). Confirm there is no inline creation form on the page, a "New Policy" button opens a modal containing the creation form, submitting or cancelling the modal returns to the full-height policies table, and the table now occupies the full page height.

**Acceptance Scenarios**:

1. **Given** the admin is on the Workflow Policies page, **When** the page loads, **Then** no inline policy-creation form is present and the policies table occupies the full page height.
2. **Given** the admin clicks "New Policy" on the Workflow Policies page, **When** the modal opens, **Then** it contains the same fields and validation previously available inline.
3. **Given** the admin submits a valid new policy in the modal, **When** submission succeeds, **Then** the modal closes and the new policy appears in the table.
4. **Given** the admin opens the modal and cancels it, **When** the modal closes, **Then** no policy is created and the table is unaffected.
5. **Given** the admin is on the Agent Policies page, **When** the admin repeats the same actions, **Then** the same modal-based behavior applies.

---

### User Story 5 - System Agents relocated in the sidebar (Priority: P3)

An administrator scanning the admin sidebar looks for "System Agents." Today it sits near the bottom of the nav list, separated from the related "Role assignments" item. The admin wants "System Agents" positioned immediately after "Role assignments," with a divider marking it as a distinct group from the remaining nav items.

**Why this priority**: Pure navigation-ordering polish with no functional dependency on the other stories; lowest impact, safe to do last.

**Independent Test**: Open the admin panel and inspect the sidebar order. Confirm "System Agents" appears immediately after "Role assignments," and a horizontal divider separates that pair from the rest of the nav items below.

**Acceptance Scenarios**:

1. **Given** the admin sidebar is rendered, **When** the admin views the nav order, **Then** "System Agents" appears directly after "Role assignments."
2. **Given** the admin sidebar is rendered, **When** the admin looks below "System Agents," **Then** a horizontal divider visually separates it from the remaining nav items.
3. **Given** the admin clicks "System Agents" in its new position, **When** navigation occurs, **Then** it behaves identically to before the move (same route, same active-state highlighting).

### Edge Cases

- What happens on an admin page whose content is naturally very short (e.g. few rows)? The table/list's own container stretches to absorb the spare height (its rows are not artificially enlarged), so the hint banner still sits directly under the table's bottom edge with no gap on any page, short or tall.
- What happens on a very short viewport (e.g. small laptop screen) where full-height content would otherwise clip the hint banner? Content area must scroll rather than clip.
- What happens on AI Providers when a provider has never been checked (no stale/healthy verdict at all)? The new staleness column must show nothing (not an empty warning state) rather than misleadingly appearing "up to date."
- What happens if the admin opens the "New Policy" modal, starts filling it in, then navigates away or closes the modal without saving? In-progress input is discarded, consistent with existing MCP servers modal behavior.
- What happens to deep links or bookmarked anchors that assumed the old inline "New Policy" form location on the page? They should still land on the Workflow/Agent Policies page; the modal simply isn't open by default.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The AI Providers table MUST display the "possibly out of date" staleness indicator in a column separate from the health status column.
- **FR-002**: The staleness indicator and its tooltip MUST NOT visually overlap the status chip or any other row's content, at any supported viewport width.
- **FR-003**: All admin panel pages MUST render the sidebar and the active tab's content stretched to fill the full available viewport height, consistent with the existing Account Settings page behavior.
- **FR-004**: When a tab's content exceeds the visible height, the content area MUST scroll independently while the sidebar remains fully visible.
- **FR-005**: On admin pages that include an informational hint/notice banner, the primary content's table/list container MUST stretch to occupy the full remaining height of the tab content area (regardless of row count), and the hint MUST render immediately below that container's bottom edge with no gap, ending just above the page's bottom edge.
- **FR-006**: Admin pages without a hint banner MUST be unaffected by the hint-positioning change.
- **FR-007**: The Workflow Policies page MUST replace its inline "New Policy" creation section with a button that opens a modal containing the same creation form and validation.
- **FR-008**: The Agent Policies page MUST replace its inline "New Policy" creation section with a button that opens a modal containing the same creation form and validation.
- **FR-009**: With the inline creation section removed, the policies table on both pages MUST occupy the full page height (subject to FR-003/FR-004).
- **FR-010**: Successfully submitting the "New Policy" modal MUST create the policy and close the modal, with the new policy visible in the table without a manual page refresh.
- **FR-011**: Cancelling or dismissing the "New Policy" modal MUST discard any in-progress input and create no policy.
- **FR-012**: The admin sidebar MUST list "System Agents" immediately after "Role assignments."
- **FR-013**: The admin sidebar MUST render a horizontal divider between the "Role assignments"/"System Agents" pair and the nav items that follow.
- **FR-014**: Moving "System Agents" in the sidebar MUST NOT change its route, permissions, or active-state highlighting behavior.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On the AI Providers page, 100% of stale-provider rows display the staleness indicator without any visual overlap with adjacent chips or rows, across standard desktop viewport widths.
- **SC-002**: Every admin panel page fills the full available viewport height with no unintended dead space, verified across at least 3 representative pages (Default models, AI Capabilities, Users).
- **SC-003**: On pages with a hint banner, the primary content is visible without scrolling past the hint first, and the hint sits with zero visible gap directly beneath the table on both the two pages currently known to have hints (Default models, AI Capabilities), even when the table has only a handful of rows.
- **SC-004**: On Workflow Policies and Agent Policies, the number of policy rows visible without scrolling increases (measured before/after on the same reference viewport) as a direct result of removing the inline creation form.
- **SC-005**: Admins can create a new workflow or agent policy end-to-end via the modal in the same or fewer steps than the previous inline form.
- **SC-006**: "System Agents" is found immediately below "Role assignments" in the sidebar by 100% of admins on first visual scan (i.e., no ambiguity in grouping, confirmed by the presence of the divider).

## Assumptions

- "Account Settings page" is the existing full-height reference layout already shipped elsewhere in the app; this feature reuses that same layout pattern for the admin shell rather than inventing a new one.
- "Same pattern as MCP servers page" means: a page-level action button opens a modal dialog containing the creation form; this feature reuses that existing MCP servers modal pattern rather than designing a new modal style.
- The "New Policy" modal's fields, validation rules, and submission behavior are unchanged from the current inline form — only its container (inline section vs. modal) changes.
- Only the Default models and AI Capabilities pages are currently known to have hint/info banners; the bottom-anchoring behavior (FR-005) is expected to apply automatically to any other admin page that has or gains such a banner, without needing page-specific rework.
- The AI Providers staleness indicator's underlying logic (when it fires, its tooltip text) is unchanged — only its column placement changes.
- "System Agents" keeps its existing icon, label, route, and permission gating; only its position and the addition of a divider change.

## Post-implementation addendum

US1-US5 above shipped as specified. Screenshot-driven review of the shipped pages surfaced further layout and correctness issues on the same admin table surfaces; these were fixed directly (not routed through a new spec) but are recorded here since they touch the same components and change user-visible behavior beyond FR-001–FR-014.

- **Per-capability model selection**: The AI Capabilities table gained a Model column — a dropdown of the assigned provider's models, defaulting to that provider's default model, so a capability can pin a different model than its provider's default (e.g. Chat on a provider's default chat model, Image generation on that provider's image model). The backend already persisted a per-capability model (`SetAiCapabilityAssignmentCommandHandler`); this was a frontend-only addition.
- **Column cleanup**: The AI Capabilities table's "Actually running on" column was removed once the Model column made it redundant. On System Agents, the "Provisioned by Ask Lucy" badge moved out of the Name cell into its own column (varying name lengths, including a GUID-suffixed one, made an inline badge land at a different x per row).
- **De-duplicated section titles**: Sections that repeated their page title inline above their own action button (MCP servers, Workflow policies, Agent policies, AI capabilities) now source that button from a portal into the shared page header (`AdminSectionActions`) instead.
- **Skeleton loading**: Every admin table shows skeleton placeholder rows while loading (`TableLoadingRow`) and a centered message when empty (`TableEmptyRow`), replacing spinners. On Default models and AI Capabilities specifically, the skeleton holds until every row's per-provider model list has resolved (via a parent-level `useQueries` sharing each row's own query key — no extra requests), not just until the first-level list arrives; without this, rows briefly painted under the still-visible skeleton with empty dropdowns, then jumped as the skeleton cleared.
- **Dropdown collapses to text when nothing is selectable**: On the AI Capabilities Model column, when the assigned provider has no model usable for that capability (or its model fetch failed), the dropdown is replaced by a plain wrapped label stating why — a disabled control opening onto an empty list was a dead end. Both control columns (Assigned provider, Model) are now pinned widths so a selection change on one row can no longer resize the table.
- **Silent-failure fix**: `ProviderDefaultModelRow`'s model-fetch failure previously rendered the same "No Available models — mark one Available on the Providers page first" caption as a provider that genuinely has none configured, sending an admin to a page where everything already looks correct. It now distinguishes a fetch failure from an empty result (constitution §"Error Handling").
