# Phase 1 Data Model: Flumeria Studio Workspace Shell

This feature introduces no database schema, no EF Core entities, and no new API payloads — it is entirely client-side UI state. "Data model" here means the shape of that client-side state (Zustand store + static config), not a persistence model. Nothing below is written to or read from SQL Server.

## ControlDefinition (static configuration, not a store)

Describes one entry point in the workspace overlay. A fixed array of these (defined in code, not fetched) drives what `WorkspaceOverlay`/`FloatingToolbar` render.

| Field | Type | Notes |
|---|---|---|
| `id` | `string` | Stable identifier, e.g. `'view-mode'`, `'layers'`, `'navigation'`, `'selection'`, `'analysis'`, `'chat'`, `'account'`. Also the key used by `workspaceOverlayStore.expandedControlId`. |
| `label` | `string` | Accessible name (`aria-label`) and visible label once expanded. |
| `icon` | `ReactNode` | MUI icon rendered in the collapsed circular state. |
| `status` | `'functional' \| 'coming-soon'` | Per FR-012/FR-021: `'coming-soon'` controls (`layers`, `navigation`, `selection`, `analysis`) render a static placeholder message instead of real content. `view-mode`, `chat`, and `account` are `'functional'` (FR-024). |
| `kind` | `'action-group' \| 'panel'` | `'action-group'` controls expand into an `ExpandableActionGroup` (view-mode, layers, navigation, selection, analysis, account — the `account` group's actions reuse `UserMenu`'s existing destinations plus the theme toggle); `'chat'` expands into a `FloatingPanel`. Determines which container `WorkspaceOverlay` renders for that control. |

**Validation rules**: `id` MUST be unique across the array (enforced by TypeScript literal union, not runtime validation — this is a fixed, code-owned list, not user input). Exactly one `ControlDefinition` has `id: 'chat'` and `kind: 'panel'`.

**State transitions**: None — this is static configuration, not stateful data.

## WorkspaceOverlayState (Zustand store: `workspaceOverlayStore`)

Session-scoped (no persistence — see research.md #4), owns which single control is expanded and the current view mode.

| Field | Type | Notes |
|---|---|---|
| `expandedControlId` | `string \| null` | The `id` of the currently expanded `ControlDefinition`, or `null` if every control is collapsed. Enforces FR-015: setting this to a new id implicitly collapses whatever was previously expanded (single field, not a set). |
| `viewMode` | `'2D' \| '3D'` | Current view-mode selection (FR-011). Defaults to `'3D'`, matching the workspace's prior all-3D presentation. |
| `unreadControlIds` | `Set<string>` (in practice, only ever contains `'chat'` today) | Generalizes `assistantPanelStore`'s `hasUnreadWhileCollapsed` — a control can be flagged as having unseen activity while collapsed. |

**Actions**:
- `expand(id: string): void` — sets `expandedControlId` to `id`, clears `id` from `unreadControlIds`.
- `collapse(): void` — sets `expandedControlId` to `null`.
- `toggle(id: string): void` — `expand(id)` if not currently expanded (or a different one is), `collapse()` if `id` is already the expanded one.
- `setViewMode(mode: '2D' | '3D'): void`.
- `markUnread(id: string): void` — adds `id` to `unreadControlIds`; only called for a control that is currently collapsed.

**State transitions**:

```text
collapsed ──expand(id)──> expanded(id) ──collapse() / toggle(id)──> collapsed
expanded(id) ──expand(otherId)──> expanded(otherId)   // previous id implicitly collapses (FR-015)
```

**Relationships**: `WorkspaceOverlay` (component) reads `expandedControlId` once and renders each `ControlDefinition` as collapsed or expanded accordingly — it does not duplicate this state locally. `AiPresenceCard` does not participate in this state machine at all (research.md #7 — it is always rendered, independent of `expandedControlId`).

## AiPresenceCard (no dedicated store)

Not stateful beyond what `useVoiceOutput` (existing, unchanged) already tracks (`isSpeaking`, `getIntensity()`). Rendering the particle sphere inside a fixed-size card instead of full-viewport is a prop/layout change to existing `SceneBackground`, not a new state concept.

## Removed: `assistantPanelStore`

`isOpen: boolean`, `hasUnreadWhileCollapsed: boolean`, `open()`, `close()`, `toggle()`, `markUnread()` are superseded by `workspaceOverlayStore` using `controlId: 'chat'` (`expandedControlId === 'chat'` replaces `isOpen`; `unreadControlIds.has('chat')` replaces `hasUnreadWhileCollapsed`). The file and its test are deleted, not deprecated in place, per constitution §2.III (no dead/parallel code left behind).
