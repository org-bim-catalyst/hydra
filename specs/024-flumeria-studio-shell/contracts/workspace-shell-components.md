# Contract: Workspace Shell Components

This feature exposes no backend API — its "interface contract" is the public prop/behavior surface of the six reusable React components it establishes (FR-016), since other features (later viewer work, and this feature's own chat integration) will build directly against these props. Types are illustrative TypeScript signatures, not final implementation code (bodies are a `/speckit-tasks` + implementation concern).

## CircularAction

The base building block: one compact circular control with a collapsed and an expanded visual state.

```ts
interface CircularActionProps {
  id: string                        // matches a ControlDefinition.id / workspaceOverlayStore key
  label: string                     // accessible name; also used as aria-label when collapsed
  icon: ReactNode
  expanded: boolean                 // controlled — driven by workspaceOverlayStore, not local state
  onToggle: () => void              // calls workspaceOverlayStore.toggle(id)
  disabled?: boolean
  badge?: boolean                   // unread indicator dot (chat's "new message" use case)
  children: ReactNode                // the content rendered once expanded (an ExpandableActionGroup or FloatingPanel)
}
```

**Contract guarantees**:
- Renders a native `<button>`-rooted MUI control (`IconButton`/`Fab`) with `aria-expanded={expanded}` and `aria-controls` pointing at the expanded content's id (FR-019).
- `Enter`/`Space` on the trigger calls `onToggle`; `Escape` while focus is anywhere inside the expanded content also calls `onToggle` (when `expanded`) and returns focus to the trigger (FR-007, US4).
- Does not manage its own expand/collapse state — `expanded` is always caller-controlled, so `WorkspaceOverlay` can enforce "only one expanded at a time" (FR-015) from a single source of truth.
- Honors `theme.transitions` for the collapsed↔expanded visual change; does not hardcode a duration (FR-008/FR-018).

## ExpandableActionGroup

Renders inside an expanded `CircularAction` when that control's `kind` is `'action-group'`.

```ts
interface ExpandableActionGroupAction {
  id: string
  label: string
  icon?: ReactNode
  onSelect?: () => void             // omitted for a 'coming-soon' control
}

interface ExpandableActionGroupProps {
  actions: ExpandableActionGroupAction[]
  status: 'functional' | 'coming-soon'
  comingSoonMessage?: string        // required when status === 'coming-soon'
}
```

**Contract guarantees**:
- When `status === 'coming-soon'`, renders `comingSoonMessage` instead of `actions`, and every action is visually/semantically non-interactive (no click handler wired) — never rendered as a disabled-looking-but-actually-broken control (FR-012).
- Actions are reachable in DOM order by keyboard (`Tab`) once the parent `CircularAction` is expanded (FR-009).

## FloatingPanel

Renders inside an expanded `CircularAction` when that control's `kind` is `'panel'` (today: only `chat`). Larger, richer content than `ExpandableActionGroup`.

```ts
interface FloatingPanelProps {
  titleId: string                   // for aria-labelledby
  onRequestClose: () => void        // Escape / close affordance inside the panel
  children: ReactNode
}
```

**Contract guarantees**:
- Moves initial focus to the first focusable element inside `children` on open, without trapping focus (the rest of the workspace remains reachable — research.md #5).
- Stays mounted (not unmounted) while collapsed, matching `AssistantPanel`'s existing "don't lose in-progress conversation state" behavior — visibility is CSS-driven (`inert`/`aria-hidden` when collapsed), not a full unmount/remount.

## FloatingToolbar

A cluster of one or more `CircularAction`s positioned over the `WorkspaceSurface` at a fixed screen location, independent of what's currently selected in the viewer.

```ts
interface FloatingToolbarProps {
  anchor: 'top-start' | 'top-end' | 'bottom-start' | 'bottom-end'
  children: ReactNode                // one or more <CircularAction>
}
```

**Contract guarantees**:
- Lays out its children responsively (FR-020) — stacks or repositions rather than overlapping at narrow viewport widths.
- Does not itself manage expand/collapse state; purely positions its `CircularAction` children.

## ContextualToolbar

Same rendering contract as `FloatingToolbar`, but its set of `CircularAction` children is expected to vary based on what's currently selected/active in the workspace (e.g., analysis actions that only make sense once something is selected). In this feature, it is established as a component (FR-016) but is not yet driven by real selection state (FR-021 — selection itself is a `'coming-soon'` placeholder).

```ts
interface ContextualToolbarProps {
  anchor: 'top-start' | 'top-end' | 'bottom-start' | 'bottom-end'
  children: ReactNode                // zero or more <CircularAction>, expected to vary by caller
}
```

## WorkspaceOverlay

The coordinating layer. Hosts every `FloatingToolbar`/`ContextualToolbar`/`FloatingPanel` above the `WorkspaceSurface` and is the one place that reads `workspaceOverlayStore.expandedControlId` to guarantee at most one control is expanded (FR-015).

```ts
interface WorkspaceOverlayProps {
  controls: ControlDefinition[]      // see data-model.md
  children?: ReactNode               // e.g. an <AiPresenceCard>, rendered outside the single-expanded-control rule
}
```

**Contract guarantees**:
- Renders one `CircularAction` per `ControlDefinition`, wiring `expanded={expandedControlId === control.id}` and `onToggle={() => toggle(control.id)}` from `workspaceOverlayStore` — no consumer of `WorkspaceOverlay` needs to touch the store directly.
- `children` (e.g. `AiPresenceCard`) render independent of the expand/collapse state machine.
