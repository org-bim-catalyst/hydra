import { Badge, Box, ClickAwayListener, Collapse, Fab } from '@mui/material'
import { type KeyboardEvent, type ReactNode, useRef } from 'react'
import { radius } from '../../theme'

export interface CircularActionProps {
  id: string
  label: string
  icon: ReactNode
  expanded: boolean
  onToggle: () => void
  disabled?: boolean
  badge?: boolean
  children: ReactNode
}

/** Fixed "control chrome" colors, sampled directly from the readdy.ai reference's
 * computed styles (`getComputedStyle`, not eyeballed off a screenshot — its own page
 * root carries a `dark` class with `background-50: oklch(0.12 0.02 280)`; what reads as
 * "light" in a screenshot is Google Maps' own light basemap tiles showing through, not
 * the app chrome). Every floating control — collapsed circle *and* expanded pill — is a
 * dark navy glass family (`background-100`/`background-200`) with near-white icon/text,
 * deliberately independent of this app's own light/dark theme toggle, exactly like the
 * reference (its buttons never changed color when its own page theme did). */
export const CIRCULAR_ACTION_CHROME = {
  collapsedBg: 'oklch(0.25 0.02 280 / 0.85)',
  collapsedHoverBg: 'oklch(0.30 0.02 280 / 0.9)',
  expandedBg: 'oklch(0.18 0.02 280 / 0.97)',
  icon: 'oklch(0.97 0.01 100)',
  border: '1px solid oklch(0.34 0.02 280 / 0.6)',
} as const

/** The base building block of the workspace-shell control system (FR-006/FR-007): a
 * compact circular trigger that grows in place into a rounded container revealing
 * `children`, and shrinks back — never a detached popover (research.md #3). `expanded`
 * is always caller-controlled (never local state) so a coordinating parent (WorkspaceOverlay)
 * can enforce "only one expanded at a time" (FR-015) from a single source of truth.
 *
 * `children` stays mounted at all times (Collapse's default `unmountOnExit={false}`,
 * plus `inert` while collapsed) rather than unmounting — required by FloatingPanel's own
 * "don't lose in-progress state while collapsed" contract, and harmless for the simpler
 * ExpandableActionGroup case. Modeled as a WAI-ARIA disclosure widget (research.md #5):
 * native `<button>` Enter/Space activation needs no extra handling, and Escape while
 * focus is inside the expanded content collapses it and returns focus to the trigger. */
export function CircularAction({
  id,
  label,
  icon,
  expanded,
  onToggle,
  disabled,
  badge,
  children,
}: CircularActionProps) {
  const triggerRef = useRef<HTMLButtonElement>(null)
  const contentId = `${id}-content`

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
          flexDirection: 'column',
          alignItems: 'flex-start',
          borderRadius: expanded ? `${radius.lg}px` : `${radius.pill}px`,
          bgcolor: expanded ? CIRCULAR_ACTION_CHROME.expandedBg : CIRCULAR_ACTION_CHROME.collapsedBg,
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
              boxShadow: 'none',
              bgcolor: 'transparent',
              color: CIRCULAR_ACTION_CHROME.icon,
              transition: (t) => t.transitions.create(['transform', 'background-color']),
              '&:hover': {
                bgcolor: CIRCULAR_ACTION_CHROME.collapsedHoverBg,
                transform: 'scale(1.05)',
              },
            }}
          >
            {icon}
          </Fab>
        </Badge>
        <Collapse in={expanded}>
          {/* Collapse only animates height — its child keeps its natural intrinsic width
              even while the Collapse itself is visually 0px tall, which would otherwise
              stretch this whole inline-flex column (and its pill/circle border-radius)
              into a pill shape at rest. Forcing width to 0 (with overflow already hidden
              on the outer Box) while collapsed removes that contribution entirely, so the
              trigger alone determines the collapsed width — a true circle. */}
          <Box
            id={contentId}
            role="group"
            aria-label={`${label} options`}
            inert={!expanded}
            sx={{
              px: 1.5,
              pb: 1.25,
              pt: 0.5,
              width: expanded ? 'auto' : 0,
              overflow: 'hidden',
              // Explicit (not relying on CSS inheritance from a MUI Typography default)
              // so every nested action label/icon reads correctly against the dark pill
              // regardless of this app's own light/dark theme.
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
