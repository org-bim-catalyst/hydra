import { Badge, Box, ClickAwayListener, Collapse, Fab } from '@mui/material'
import { type KeyboardEvent, type ReactNode, useRef } from 'react'
import { radius } from '../../theme'

/** Direction the ribbon expands relative to the trigger button, derived from the
 * button's screen placement: right-edge → left, top → down, bottom → up, left → right. */
export type ExpandDirection = 'left' | 'right' | 'up' | 'down'

export interface CircularActionProps {
  id: string
  label: string
  icon: ReactNode
  expanded: boolean
  onToggle: () => void
  disabled?: boolean
  badge?: boolean
  children: ReactNode
  /** Defaults to 'down'. WorkspaceOverlay derives this from control.placement. */
  expandDirection?: ExpandDirection
  /** When true the trigger does NOT turn green on expand — use for controls that should
   * look the same regardless of expanded state (e.g. the Account menu). */
  noTriggerAccent?: boolean
}

/** Shared chrome constants consumed by sibling components (chat panel, viewer widgets)
 * that were built before per-theme sx callbacks were adopted. `CircularAction` itself no
 * longer reads these — it uses inline `(t) =>` callbacks so the ribbon adapts to the
 * active theme. The dark-mode values here keep those other consumers compiling unchanged. */
export const CIRCULAR_ACTION_CHROME = {
  collapsedBg: '#45454D',
  collapsedHoverBg: 'oklch(0.30 0.02 280 / 0.9)',
  expandedBg: 'oklch(0.18 0.02 280 / 0.97)',
  expandedTriggerBg: '#2E7F26',
  expandedTriggerHoverBg: '#3a6b1f',
  icon: 'oklch(0.97 0.01 100)',
  border: '1px solid oklch(0.34 0.02 280 / 0.6)',
} as const

/** The base building block of the workspace-shell control system.
 *
 * The trigger Fab is always in-flow (its size never changes), so it never nudges its
 * siblings. The expanded content is rendered in a `position:absolute` overlay positioned
 * in the direction requested — it overlays the workspace rather than pushing other
 * controls. Colors adapt to the app's light/dark theme via MUI sx callbacks. */
export function CircularAction({
  id,
  label,
  icon,
  expanded,
  onToggle,
  disabled,
  badge,
  children,
  expandDirection = 'down',
  noTriggerAccent = false,
}: CircularActionProps) {
  const triggerRef = useRef<HTMLButtonElement>(null)
  const contentId = `${id}-content`

  const isHorizontal = expandDirection === 'left' || expandDirection === 'right'
  const collapseOrientation: 'horizontal' | 'vertical' = isHorizontal ? 'horizontal' : 'vertical'

  // Position the overlay relative to the trigger circle (position:relative parent).
  // right:'100%' places the content's right edge at the trigger's left edge (expands left).
  // top:'100%' places the content's top edge at the trigger's bottom edge (expands down).
  const overlayPositionSx =
    expandDirection === 'left'  ? { top: 0, right: '100%' } :
    expandDirection === 'right' ? { top: 0, left: '100%' }  :
    expandDirection === 'up'    ? { bottom: '100%', right: 0 } :
                                  { top: '100%', right: 0 }    // 'down'

  // Padding within the pill — breathing room on the sides that face away from the trigger.
  const contentPadding =
    expandDirection === 'left'  ? { py: 0.75, pl: 1.5, pr: 0.5 } :
    expandDirection === 'right' ? { py: 0.75, pl: 0.5, pr: 1.5 } :
    expandDirection === 'up'    ? { px: 1.5, pt: 1.25, pb: 0.5 } :
                                  { px: 1.5, pb: 1.25, pt: 0.5 }  // 'down'

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && expanded) {
      event.stopPropagation()
      onToggle()
      triggerRef.current?.focus()
    }
  }

  return (
    <ClickAwayListener onClickAway={() => expanded && onToggle()}>
      {/* Outer Box is ONLY the trigger's in-flow footprint — never resizes on expand.
          position:relative is the containing block for the absolute overlay below. */}
      <Box onKeyDown={handleKeyDown} sx={{ position: 'relative', display: 'inline-flex' }}>
        <Badge
          color="secondary"
          variant="dot"
          overlap="circular"
          invisible={!badge}
          sx={{ alignSelf: 'flex-start' }}
        >
          <Fab
            ref={triggerRef}
            size="medium"
            aria-label={label}
            aria-expanded={expanded}
            aria-controls={contentId}
            onClick={onToggle}
            disabled={disabled}
            sx={{
              boxShadow: '0 2px 8px rgba(0,0,0,0.28)',
              // Collapsed: theme-aware (dark glass in dark mode, white glass in light mode).
              // Expanded + accent: brand green (#2E7F26). Expanded + noTriggerAccent: keep collapsed color.
              bgcolor: expanded && !noTriggerAccent
                ? CIRCULAR_ACTION_CHROME.expandedTriggerBg
                : (t) => t.palette.mode === 'dark'
                  ? 'rgba(69,69,77,0.92)'
                  : 'rgba(255,255,255,0.90)',
              color: expanded && !noTriggerAccent
                ? '#fff'
                : (t) => t.palette.mode === 'dark'
                  ? 'oklch(0.97 0.01 100)'
                  : 'rgba(0,0,0,0.72)',
              border: (t) => t.palette.mode === 'dark'
                ? '1px solid oklch(0.34 0.02 280 / 0.5)'
                : '1px solid rgba(0,0,0,0.12)',
              backdropFilter: 'blur(8px)',
              transition: (t) => t.transitions.create(['transform', 'background-color', 'color', 'box-shadow']),
              '&:hover': {
                bgcolor: expanded && !noTriggerAccent
                  ? CIRCULAR_ACTION_CHROME.expandedTriggerHoverBg
                  : (t) => t.palette.mode === 'dark'
                    ? 'oklch(0.30 0.02 280 / 0.92)'
                    : 'rgba(255,255,255,0.98)',
                transform: 'scale(1.05)',
                boxShadow: '0 4px 12px rgba(0,0,0,0.34)',
              },
            }}
          >
            {icon}
          </Fab>
        </Badge>

        {/* Absolutely-positioned overlay — never participates in layout, never pushes
            sibling controls. `pointerEvents:none` when collapsed prevents ghost hit-areas. */}
        <Box
          sx={{
            position: 'absolute',
            zIndex: 1300,
            pointerEvents: expanded ? 'auto' : 'none',
            ...overlayPositionSx,
          }}
        >
          <Collapse in={expanded} orientation={collapseOrientation}>
            <Box
              id={contentId}
              role="group"
              aria-label={`${label} options`}
              inert={!expanded}
              sx={{
                ...contentPadding,
                bgcolor: (t) => t.palette.mode === 'dark'
                  ? 'oklch(0.18 0.02 280 / 0.97)'
                  : 'rgba(255,255,255,0.96)',
                border: (t) => t.palette.mode === 'dark'
                  ? '1px solid oklch(0.34 0.02 280 / 0.6)'
                  : '1px solid rgba(0,0,0,0.12)',
                borderRadius: `${radius.pill}px`,
                backdropFilter: 'blur(12px)',
                boxShadow: '0 4px 16px rgba(0,0,0,0.28)',
                color: (t) => t.palette.mode === 'dark'
                  ? 'oklch(0.97 0.01 100)'
                  : 'rgba(0,0,0,0.87)',
                whiteSpace: 'nowrap',
              }}
            >
              {children}
            </Box>
          </Collapse>
        </Box>
      </Box>
    </ClickAwayListener>
  )
}
