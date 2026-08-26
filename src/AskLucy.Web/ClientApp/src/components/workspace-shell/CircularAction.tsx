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
  /** Defaults to 'down' (existing behavior). WorkspaceOverlay derives this from placement. */
  expandDirection?: ExpandDirection
}

/** Fixed "control chrome" colors — deliberately independent of the app's light/dark theme
 * (matching the established convention: these controls always render as a dark glass family
 * regardless of the page theme). Collapsed button: #45454D per design spec. Expanded
 * trigger: #2E7F26 (green) per design spec. */
export const CIRCULAR_ACTION_CHROME = {
  collapsedBg: '#45454D',
  collapsedHoverBg: 'oklch(0.30 0.02 280 / 0.9)',
  expandedBg: 'oklch(0.18 0.02 280 / 0.97)',
  expandedTriggerBg: '#2E7F26',
  expandedTriggerHoverBg: '#3a6b1f',
  icon: 'oklch(0.97 0.01 100)',
  border: '1px solid oklch(0.34 0.02 280 / 0.6)',
} as const

/** The base building block of the workspace-shell control system (FR-006/FR-007): a
 * compact circular trigger that expands into a ribbon — a single-row rounded-rect pill —
 * in the direction specified by `expandDirection`. Direction is determined by placement:
 * right-edge buttons expand left (horizontal ribbon), top buttons expand down, bottom
 * buttons expand up. `expanded` is always caller-controlled (WorkspaceOverlay enforces
 * "only one expanded at a time" via workspaceOverlayStore). Modeled as a WAI-ARIA
 * disclosure widget: Escape while focus is inside the expanded content collapses it and
 * returns focus to the trigger. */
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
}: CircularActionProps) {
  const triggerRef = useRef<HTMLButtonElement>(null)
  const contentId = `${id}-content`

  const isHorizontal = expandDirection === 'left' || expandDirection === 'right'

  const flexDir =
    expandDirection === 'left' ? 'row-reverse' :
    expandDirection === 'right' ? 'row' :
    expandDirection === 'up' ? 'column-reverse' :
    'column'

  const collapseOrientation: 'horizontal' | 'vertical' = isHorizontal ? 'horizontal' : 'vertical'

  // Padding on the content Box — pushed to the side opposite the trigger so the icons
  // have breathing room within the pill and aren't flush against the trigger circle.
  const contentPadding =
    expandDirection === 'left'  ? { py: 1.25, pl: 1.5,  pr: 0.5 } :
    expandDirection === 'right' ? { py: 1.25, pl: 0.5,  pr: 1.5 } :
    expandDirection === 'up'    ? { px: 1.5,  pt: 1.25, pb: 0.5 } :
                                  { px: 1.5,  pb: 1.25, pt: 0.5 } // 'down'

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && expanded) {
      event.stopPropagation()
      onToggle()
      triggerRef.current?.focus()
    }
  }

  return (
    <ClickAwayListener onClickAway={() => expanded && onToggle()}>
      <Box
        onKeyDown={handleKeyDown}
        sx={{
          display: 'inline-flex',
          flexDirection: flexDir,
          // Horizontal ribbons center the trigger and pill vertically; vertical ribbons
          // left-align so the trigger stays flush with the left edge of the pill.
          alignItems: isHorizontal ? 'center' : 'flex-start',
          // Horizontal ribbons keep pill shape always (circle → wider pill, never a box).
          // Vertical ribbons: collapsed = circle, expanded = rounded-rect box.
          borderRadius: expanded && !isHorizontal ? `${radius.lg}px` : `${radius.pill}px`,
          // Outer Box carries the dark glass when expanded (forms the ribbon pill background).
          // When collapsed the Fab carries the color; outer Box is transparent.
          bgcolor: expanded ? CIRCULAR_ACTION_CHROME.expandedBg : 'transparent',
          border: CIRCULAR_ACTION_CHROME.border,
          backdropFilter: 'blur(12px)',
          boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
          overflow: 'hidden',
          transition: (t) => t.transitions.create(['border-radius', 'background-color']),
        }}
      >
        <Badge
          color="secondary"
          variant="dot"
          overlap="circular"
          invisible={!badge}
          sx={{ alignSelf: isHorizontal ? 'center' : 'flex-start' }}
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
              boxShadow: 'none',
              // Collapsed: #45454D (solid dark-gray). Expanded: #2E7F26 (green) per design spec.
              bgcolor: expanded ? CIRCULAR_ACTION_CHROME.expandedTriggerBg : CIRCULAR_ACTION_CHROME.collapsedBg,
              color: CIRCULAR_ACTION_CHROME.icon,
              transition: (t) => t.transitions.create(['transform', 'background-color']),
              '&:hover': {
                bgcolor: expanded ? CIRCULAR_ACTION_CHROME.expandedTriggerHoverBg : CIRCULAR_ACTION_CHROME.collapsedHoverBg,
                transform: 'scale(1.05)',
              },
            }}
          >
            {icon}
          </Fab>
        </Badge>
        <Collapse in={expanded} orientation={collapseOrientation}>
          {/* Vertical Collapse animates height but the content retains its natural width
              while collapsed (height=0), which would stretch the outer Box horizontally.
              `width: 0` when collapsed prevents this. Horizontal Collapse is the mirror
              case — content retains its natural height while width=0, which would inflate
              the pill taller than the trigger circle. `height: 0` prevents that. */}
          <Box
            id={contentId}
            role="group"
            aria-label={`${label} options`}
            inert={!expanded}
            sx={{
              ...contentPadding,
              width: !isHorizontal ? (expanded ? 'auto' : 0) : undefined,
              height: isHorizontal ? (expanded ? 'auto' : 0) : undefined,
              overflow: 'hidden',
              color: CIRCULAR_ACTION_CHROME.icon,
            }}
          >
            {children}
          </Box>
        </Collapse>
      </Box>
    </ClickAwayListener>
  )
}
