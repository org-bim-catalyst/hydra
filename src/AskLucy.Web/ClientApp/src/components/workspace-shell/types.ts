import type { ReactNode } from 'react'

export type ControlStatus = 'functional' | 'coming-soon'
export type ControlKind = 'action-group' | 'panel'
/** Which cluster `WorkspaceOverlay` groups this control into (readdy.ai reference: an
 * avatar+account cluster top-right, a vertical stack of tool icons below it, and a single
 * chat trigger bottom-right — not one crowded row). */
export type ControlPlacement = 'top-cluster' | 'right-stack' | 'bottom-end'

/** One entry point in the workspace overlay (data-model.md). A fixed, code-owned list —
 * not fetched or user-editable — drives what `WorkspaceOverlay` renders. `content` is the
 * implementation-level completion of contracts/workspace-shell-components.md's
 * `WorkspaceOverlayProps` (which defers per-control content to the implementation): the
 * `ExpandableActionGroup`/`FloatingPanel` element rendered inside that control's
 * `CircularAction` once expanded. */
export interface ControlDefinition {
  id: string
  label: string
  icon: ReactNode
  status: ControlStatus
  kind: ControlKind
  placement: ControlPlacement
  content: ReactNode
}
