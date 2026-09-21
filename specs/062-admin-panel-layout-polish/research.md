# Phase 0 Research: Admin Panel Layout & Polish Pass

All Technical Context fields were resolved directly from the existing codebase (no NEEDS CLARIFICATION remain). This document records the design decisions for each user story.

## Decision 1: Staleness indicator extraction (US1)

**Decision**: Extract the "Possibly out of date" `Chip` + `Tooltip` out of `ProviderHealthCell.tsx` into a new sibling cell component (`ProviderStalenessCell.tsx`), rendered by `AdminAiProvidersPage.tsx` in a new table column placed immediately after the existing Health column. The `isStale` computation (`provider.healthStaleAfterUtc` vs. current time) moves with it unchanged.

**Rationale**: The overlap bug is caused by two chips sharing one `flexWrap: 'wrap'` cell; giving the staleness indicator its own column removes the wrap entirely and matches the existing table's one-concept-per-column convention (Provider / Enabled / Credential / Health already each get their own column).

**Alternatives considered**:
- *Keep one cell, stop wrapping (`flexWrap: 'nowrap'`)* — rejected: would just make the existing cell overflow/truncate on narrow viewports instead of fixing the row-overlap; doesn't address the tooltip-overlaps-next-row problem, which is a stacking/height issue tied to two chips occupying one cell.
- *Remove the chip entirely* — rejected: confirmed functional (live-computed against `healthStaleAfterUtc`), not decorative; the user's own message conditioned removal on it being non-functional.

## Decision 2: Full-height admin shell (US2)

**Decision**: In `AdminShell.tsx`, change the outer flex row from `alignItems: 'flex-start'` to `alignItems: 'stretch'`, and give the content slot `Box` the same `flex: 1, minHeight: 0, display: 'flex', flexDirection: 'column'` treatment that `SettingsPage.tsx` already applies to its `Paper`. Each admin page's own root element then becomes responsible for taking `flex: 1, minHeight: 0` and (where it has a table) wrapping that table in a container with `overflow: 'auto'`, mirroring `SettingsPage.tsx`'s `Paper` → `Tabs` → scrollable `Box` pattern exactly.

**Rationale**: `AppShell.tsx` (the outermost shell) already establishes a full-height flex column context — confirmed by reading it in full; the bug is entirely localized to `AdminShell`'s inner row not stretching its children. Reusing the exact idiom already shipped and accepted on Account Settings satisfies the user's explicit "matching what was done on the Account Settings page" instruction and avoids inventing a second layout convention.

**Alternatives considered**:
- *`height: 100%` on children instead of flex* — rejected: the codebase's established idiom (seen in both `AppShell.tsx` and `SettingsPage.tsx`) is `flex: 1` + `minHeight: 0`, not percentage heights, which are fragile against `100dvh`-based mobile viewport quirks already being guarded against in `AppShell.tsx`.
- *Fixed sidebar height via `100vh` calc* — rejected: breaks the existing sticky-header behavior (`position: sticky, top: 72`) and doesn't compose with `AppShell`'s existing sticky header height accounting.

**Responsive note**: Below the breakpoint where the sidebar collapses to icons-only (existing `AdminShell` behavior, unchanged by this feature), `alignItems: 'stretch'` continues to apply — both panes still stretch to the shorter viewport's available height and each scrolls internally via `overflow: 'auto'`, so no new breakpoint-specific rule is needed.

## Decision 3: Hint anchored under a full-height table (US3)

**Decision**: On each admin page with a hint (currently `AdminDefaultModelsPage.tsx`, `AdminAiCapabilitiesPage.tsx`), reorder the JSX so the table/list container renders first and is given `flex: 1, minHeight: 0, overflow: 'auto'` (so it always occupies whatever height the tab content area has, independent of row count), and move the `Alert` hint to render immediately after it with no intervening margin (`mt: 0` on the alert, `mb: 0` on the table's wrapper), so the hint sits flush under the table and just above the page's own bottom padding.

**Rationale**: This directly implements the user's clarification: "the tables will occupy the full height of the page so the hint will be under the table bottom and before the page bottom, in all pages" — the stretching behavior belongs to the table container itself (it absorbs spare height), not to the hint being pinned via `position: sticky`/`absolute`. This keeps the hint in normal document flow, which is simpler and avoids z-index/overlap edge cases a pinned element would introduce.

**Alternatives considered**:
- *Hint pinned to viewport bottom via sticky/absolute positioning* — rejected per the user's explicit correction; also would risk overlapping short tables' empty space or requiring z-index management against the sidebar.
- *Only stretch the table when content already exceeds the viewport* — rejected per the user's explicit correction: the table must always stretch "regardless of row count," so a short table (e.g. 3 providers) still pushes the hint to the bottom rather than leaving a gap.

## Decision 4: Policy creation forms as modals (US4)

**Decision**: Create two new dialog components, `WorkflowPolicyFormDialog.tsx` and `AgentPolicyFormDialog.tsx`, each following the exact prop shape and structure already established by `McpServerForm.tsx` (`open`, `isSaving`, `errorMessage`, `onClose`, `onSubmit`; no `server`/edit-mode prop needed since neither existing panel has an edit action today — confirmed by reading both panels in full, both only expose toggle-enabled and delete). `WorkflowPolicyAdminPanel.tsx` and `AgentPolicyAdminPanel.tsx` drop their inline "New Policy" `Paper` section, replace it with a header `Stack` (title + "New policy" `Button`, mirroring `McpServerList.tsx`'s header row), keep their existing `createPolicy` mutation logic (moved into the new dialog component or left in the panel and passed down via `onSubmit` — implementation detail for tasks phase), and let their `TableContainer` grow to fill the now-available height per Decision 2's flex pattern.

**Rationale**: The two policy panels have genuinely different field sets (Workflow: name, node type, underlying tool name, description, conditions JSON; Agent: name, tool name, description, conditions JSON — confirmed by reading both files), so per the constitution's Simplicity/YAGNI guidance (§ Principle III) this is modeled as two small, separate dialog components rather than one generic "policy form" abstraction forced across two not-quite-identical shapes. Both still share the same interaction pattern (button opens dialog, dialog owns the create mutation, table stays visible underneath), satisfying the user's "same pattern as MCP servers page" instruction without a premature shared abstraction.

**Alternatives considered**:
- *One generic `<PolicyFormDialog policyType="workflow"|"agent">` component* — rejected: the field-level differences (node-type select + underlying-tool-name vs. single tool-name field) would force conditional rendering branches inside one component, which is more complex than two small, independently readable components for only two call sites.
- *Keep the create form inline but collapsible* — rejected: doesn't fully free the vertical space the user asked for ("so the table can occupy the full page height"), and diverges from the explicitly named MCP-servers reference pattern.

## Decision 5: Sidebar reorder + divider (US5)

**Decision**: Add an optional `dividerAfter?: boolean` field to the `AdminNavItem` interface in `adminNav.tsx`. Reorder `ADMIN_NAV` so "System agents" moves to immediately after "Role assignments" and set `dividerAfter: true` on the "System agents" entry. In `AdminShell.tsx`'s nav-rendering `.map()`, render a MUI `Divider` immediately after any item whose `dividerAfter` is true.

**Rationale**: `ADMIN_NAV` is already a flat, declarative array consumed by a single `.map()` in `AdminShell.tsx`; adding one optional boolean field is the minimal change consistent with the existing data-driven nav pattern (no new component, no parallel nav-groups concept needed for a single divider).

**Alternatives considered**:
- *Introduce a `NavGroup[]` wrapper structure (array of arrays)* — rejected as over-engineering for a single divider; would force every other unaffected list consumer (permission filtering, `visibleNav` computation) to be rewritten for no behavioral gain (YAGNI).
