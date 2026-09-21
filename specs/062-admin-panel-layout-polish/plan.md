# Implementation Plan: Admin Panel Layout & Polish Pass

**Branch**: `062-admin-panel-layout-polish` | **Date**: 2026-09-20 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/062-admin-panel-layout-polish/spec.md`

## Summary

Five independent, frontend-only fixes to the admin panel's information density and layout consistency: split the AI Providers "possibly out of date" staleness indicator into its own table column; make every admin page's sidebar + tab content stretch to the full available height (reusing the flex pattern already shipped on the Account Settings page); make each admin table container absorb all spare height so an existing hint banner sits with zero gap directly beneath it and just above the page bottom; convert the Workflow Policies and Agent Policies inline "New Policy" forms into modals (reusing the MCP servers page's register/edit dialog pattern) so their tables get the full page height; and reorder "System Agents" to sit directly after "Role assignments" in the admin sidebar, separated from the rest of the nav by a divider. No backend, API, or domain changes — this is a UI/layout-only pass over existing components and existing endpoints.

## Technical Context

**Language/Version**: TypeScript 5.x (React 19), strict mode

**Primary Dependencies**: React, Material UI (MUI) v6, TanStack Query, React Router — all already in use by the touched components; no new dependency introduced

**Storage**: N/A — no new persisted state; the existing `ask-lucy.admin-sidebar-collapsed` localStorage key and existing policy/provider API endpoints are unchanged

**Testing**: Vitest + React Testing Library (existing project convention — see `ProviderHealthCell.test.tsx`, `AdminShell.test.tsx`, `AdminShell.a11y.test.tsx`)

**Target Platform**: Web — admin panel routes only (`/admin/*`), desktop-first per existing admin UX, must remain responsive per constitution §7

**Project Type**: Web application — this feature is frontend-only (`src/AskLucy.Web/ClientApp`); no backend project is touched

**Performance Goals**: No regression — this is a layout/CSS and component-composition change with no new network calls; modal conversion removes DOM (an always-mounted inline form) rather than adding it

**Constraints**: Must not change any existing API contract or request/response shape (policy create/update endpoints are reused as-is through the new modals); must preserve existing keyboard/focus/a11y behavior for the sidebar and any dialog; must not regress the existing `AdminShell` collapse/expand behavior

**Scale/Scope**: 6 admin pages directly touched (AI Providers, Default models, AI Capabilities, Workflow Policies, Agent Policies) plus the shared `AdminShell` layout and `ADMIN_NAV` list that every admin page (~11 sections) renders through

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **§3 Clean Architecture / Dependency Rule** — N/A. No Domain, Application, Infrastructure, or Api project is touched; this feature lives entirely in `AskLucy.Web/ClientApp`. **PASS.**
- **§5 Database Principles** — N/A. No entity, migration, or schema change. **PASS.**
- **§6 API Standards** — N/A. No endpoint is added, removed, or changed; the new policy-creation modals call the same existing `createWorkflowPolicy`/`createAgentPolicy` mutations the inline forms already called. **PASS.**
- **§7 UI Principles** —
  - *Design system*: the modal pattern reuses the existing `McpServerForm`/`Dialog` pattern rather than inventing a new one (design system rule: compose from existing patterns before writing a bespoke component). **PASS.**
  - *Accessibility*: full-height flex changes and the new staleness column must preserve existing keyboard operability, ARIA roles, and focus order; MUI `Dialog` already provides focus trapping. New/changed a11y-relevant surfaces get a11y test coverage per §10. **PASS, verified in Phase 1 design and again at implementation via existing `*.a11y.test.tsx` convention.**
  - *Responsive design*: full-height stretching must degrade gracefully below desktop breakpoints (admin panel is desktop-first but must not literally break). **PASS, addressed in research.md Decision 2.**
  - *Theming*: no new hardcoded colors introduced. **PASS.**
- **§8 Security** — N/A. No auth, data-access, file-handling, or AI tool/agent capability surface is touched; policy creation continues to go through the same authorized endpoints. **PASS.**
- **§10 Testing Standards** — New/changed behavior (column split, layout reflow, modal open/submit/cancel, nav reorder) requires test updates in the same change, per Principle III (§18 AI Coding Agent Rules). Addressed per-story in tasks. **PASS, enforced at task-generation time.**

**Result: PASS — no violations, no Complexity Tracking entries required.**

## Project Structure

### Documentation (this feature)

```text
specs/062-admin-panel-layout-polish/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output (/speckit-plan command)
├── data-model.md         # Phase 1 output (/speckit-plan command)
├── quickstart.md        # Phase 1 output (/speckit-plan command)
├── contracts/           # N/A for this feature — no API contract changes (see Structure Decision)
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
src/AskLucy.Web/ClientApp/src/
├── features/
│   ├── admin/
│   │   ├── adminNav.tsx                              # ADMIN_NAV list — reorder + new dividerAfter flag
│   │   ├── components/
│   │   │   ├── AdminShell.tsx                         # sidebar/content full-height stretch + divider rendering
│   │   │   ├── ProviderHealthCell.tsx                 # loses the staleness chip
│   │   │   └── ProviderStalenessCell.tsx               # NEW — extracted staleness indicator + tooltip
│   │   └── pages/
│   │       ├── AdminAiProvidersPage.tsx                # new "Last confirmed"/staleness column header + cell
│   │       ├── AdminDefaultModelsPage.tsx              # table stretches, hint moves after it
│   │       └── AdminAiCapabilitiesPage.tsx             # table stretches, hint moves after it
│   ├── workflows/
│   │   └── components/
│   │       ├── WorkflowPolicyAdminPanel.tsx            # inline form removed, table stretches
│   │       └── WorkflowPolicyFormDialog.tsx            # NEW — modal, mirrors McpServerForm's shape
│   └── agents/
│       └── components/
│           ├── AgentPolicyAdminPanel.tsx               # inline form removed, table stretches
│           └── AgentPolicyFormDialog.tsx                # NEW — modal, mirrors McpServerForm's shape
└── (corresponding *.test.tsx / *.a11y.test.tsx files updated alongside each component above)
```

**Structure Decision**: This is a frontend-only feature entirely within `src/AskLucy.Web/ClientApp/src/features/{admin,workflows,agents}`. No backend project (`Domain`/`Application`/`Infrastructure`/`Api`) is touched, and no new or changed API contract is introduced, so `contracts/` is not populated — the feature reuses existing endpoints (`workflowPoliciesApi`, `agentPoliciesApi`) unchanged. `AdminShell` and `adminNav.tsx` are shared infrastructure touched once and consumed by every admin page, per the existing "one shared shell/nav list" convention already established in this codebase (see `AdminShell.tsx`'s own doc comment).

## Complexity Tracking

*No Constitution Check violations — this section is intentionally empty.*
