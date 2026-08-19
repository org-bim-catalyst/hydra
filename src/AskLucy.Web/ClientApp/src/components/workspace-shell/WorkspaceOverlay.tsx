import { Box } from '@mui/material'
import type { ReactNode } from 'react'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { CircularAction } from './CircularAction'
import { FloatingToolbar } from './FloatingToolbar'
import type { ControlDefinition, ControlPlacement } from './types'

export interface WorkspaceOverlayProps {
  controls: ControlDefinition[]
  /** Non-`CircularAction` items rendered ahead of the `top-cluster` controls in the same
   * row (e.g. `ThemeToggleButton` — a direct-action button, not a disclosure widget, so
   * it isn't a `ControlDefinition`). */
  topClusterLeading?: ReactNode
  children?: ReactNode
}

function groupByPlacement(controls: ControlDefinition[]): Record<ControlPlacement, ControlDefinition[]> {
  const groups: Record<ControlPlacement, ControlDefinition[]> = {
    'top-cluster': [],
    'right-stack': [],
    'bottom-end': [],
  }
  for (const control of controls) {
    groups[control.placement].push(control)
  }
  return groups
}

/** The coordinating layer (FR-015/FR-016): hosts every circular control above the
 * `WorkspaceSurface` and is the one place that reads `workspaceOverlayStore`, so no
 * consumer needs to touch the store directly. Groups `controls` into up to three
 * clusters (readdy.ai reference): a top-right row (`top-cluster` — theme/account), a
 * vertical stack below it (`right-stack` — viewer tools), and a single bottom-right
 * trigger (`bottom-end` — chat). Transparent/pointer-events-none outside its own
 * controls (mirroring `MinimalTopBar`'s prior convention) so the workspace surface
 * beneath stays interactive through the empty space. `children` (e.g. `AiPresenceCard`)
 * render independent of the expand/collapse state machine. */
export function WorkspaceOverlay({ controls, topClusterLeading, children }: WorkspaceOverlayProps) {
  const expandedControlId = useWorkspaceOverlayStore((s) => s.expandedControlId)
  const unreadControlIds = useWorkspaceOverlayStore((s) => s.unreadControlIds)
  const toggle = useWorkspaceOverlayStore((s) => s.toggle)
  const groups = groupByPlacement(controls)

  const renderControl = (control: ControlDefinition) => (
    <CircularAction
      key={control.id}
      id={control.id}
      label={control.label}
      icon={control.icon}
      expanded={expandedControlId === control.id}
      onToggle={() => toggle(control.id)}
      badge={unreadControlIds.has(control.id)}
    >
      {control.content}
    </CircularAction>
  )

  return (
    <Box sx={{ position: 'absolute', inset: 0, zIndex: 2, pointerEvents: 'none' }}>
      {(topClusterLeading || groups['top-cluster'].length > 0) && (
        <Box sx={{ pointerEvents: 'auto' }}>
          <FloatingToolbar anchor="top-end">
            {topClusterLeading}
            {groups['top-cluster'].map(renderControl)}
          </FloatingToolbar>
        </Box>
      )}
      {groups['right-stack'].length > 0 && (
        <Box sx={{ pointerEvents: 'auto' }}>
          <FloatingToolbar anchor="top-end" direction="column" sx={{ mt: { xs: 9, sm: 10.5 } }}>
            {groups['right-stack'].map(renderControl)}
          </FloatingToolbar>
        </Box>
      )}
      {groups['bottom-end'].length > 0 && (
        <Box sx={{ pointerEvents: 'auto' }}>
          <FloatingToolbar anchor="bottom-end">{groups['bottom-end'].map(renderControl)}</FloatingToolbar>
        </Box>
      )}
      {children}
    </Box>
  )
}
