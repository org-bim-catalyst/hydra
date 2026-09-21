# Phase 1 Data Model: Admin Panel Layout & Polish Pass

## Summary

This feature is a UI/layout-only change. It introduces **no new domain entities, no database changes, and no API request/response shape changes**. The only interface-level change is a small addition to an existing frontend-only navigation type.

## Frontend type change

### `AdminNavItem` (src/AskLucy.Web/ClientApp/src/features/admin/adminNav.tsx)

Existing shape (unchanged fields):

| Field | Type | Notes |
|---|---|---|
| `path` | `string?` | Route path, mutually relevant with `onSelect` |
| `id` | `string?` | Identifier for action-triggered items (e.g. `hangfire-dashboard`) |
| `label` | `string` | Display label |
| `icon` | `ReactNode` | Nav icon |
| `permission` | `string \| string[]?` | Gates visibility |
| `builtInOnly` | `boolean?` | Gates visibility to built-in admins |
| `onSelect` | `() => void?` | Handler for action-triggered items |

**New field**:

| Field | Type | Notes |
|---|---|---|
| `dividerAfter` | `boolean?` | When `true`, `AdminShell.tsx` renders a `Divider` immediately after this item's `ListItem`. Defaults to falsy/absent (no divider) for every existing item. Set only on the "System agents" entry per US5. |

No other entity, table, or contract is affected by this feature. Existing `WorkflowPolicy`/`AgentPolicy`/`AiProvider` types and their API contracts are reused as-is by the new modal components (Decision 4 in [research.md](./research.md)) and the new staleness cell (Decision 1).
