import type { ReactNode } from 'react'
import { FloatingToolbar, type FloatingToolbarAnchor } from './FloatingToolbar'

export interface ContextualToolbarProps {
  anchor: FloatingToolbarAnchor
  children: ReactNode
}

/** Same rendering contract as `FloatingToolbar`, but semantically distinct: its
 * `CircularAction` children are expected to vary based on what's currently
 * selected/active in the workspace (e.g. analysis actions that only appear once
 * something is selected), rather than being a fixed cluster. Established per FR-016 as
 * a reusable primitive for later features — this feature does not yet drive it from
 * real selection state (FR-021; selection itself is a `'coming-soon'` placeholder), so
 * it is not mounted anywhere in `ChatPage.tsx` yet. Delegates its actual layout to
 * `FloatingToolbar` rather than duplicating the same positioning logic (constitution
 * §2.III DRY) — the two names exist to signal intent to callers, not because the
 * rendering differs. */
export function ContextualToolbar({ anchor, children }: ContextualToolbarProps) {
  return <FloatingToolbar anchor={anchor}>{children}</FloatingToolbar>
}
