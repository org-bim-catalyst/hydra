import { Badge, Box, ClickAwayListener, Collapse, Fab } from '@mui/material'
import { type KeyboardEvent, type ReactNode, useRef } from 'react'
import { radius } from '../../theme'

/** Direction the ribbon expands relative to the trigger button, derived from placement:
 * right-edge → left, top → down, bottom → up, left → right. */
export type ExpandDirection = 'left' | 'right' | 'up' | 'down'

/** Controls the expanded-content style:
 * - 'pill': content wraps around the Fab so the pill's rounded edge aligns with the
 *   Fab's circular border (the sliding ribbon pattern).
 * - 'card': content drops below (or adjacent to) the Fab as a standalone dropdown card,
 *   without overlapping the Fab (the account-menu pattern). */
export type ContentShape = 'pill' | 'card'

export interface CircularActionProps {
  id: string
  label: string
  icon: ReactNode
  expanded: boolean
  onToggle: () => void
  disabled?: boolean
  badge?: boolean
  children: ReactNode
  expandDirection?: ExpandDirection
  /** Suppress the green accent on the trigger when expanded (e.g. account menu). */
  noTriggerAccent?: boolean
  /** Default 'pill'. Use 'card' for dropdown-style menus (account, settings). */
  contentShape?: ContentShape
}

/** Shared chrome tokens consumed by sibling workspace-shell components that predate the
 * per-theme sx-callback approach adopted in SPEC-041. `CircularAction` itself no longer
 * reads these — it uses inline `(t) =>` callbacks. The dark-mode values here keep those
 * other consumers compiling without change. */
export const CIRCULAR_ACTION_CHROME = {
  collapsedBg: '#45454D',
  collapsedHoverBg: 'oklch(0.30 0.02 280 / 0.9)',
  expandedBg: 'oklch(0.18 0.02 280 / 0.97)',
  expandedTriggerBg: '#2E7F26',
  expandedTriggerHoverBg: '#3a6b1f',
  icon: 'oklch(0.97 0.01 100)',
  border: '1px solid oklch(0.34 0.02 280 / 0.6)',
} as const

/** FAB_PX: the trigger Fab's width/height (40 px, same as option IconButtons so all
 * items in the ribbon are the same visual size). Used to calculate pill padding. */
const FAB_PX = 40
/** GAP_PX: gap inside the pill between the last option icon and the Fab's edge. */
const GAP_PX = 8
/** TRIGGER_RESERVE: total padding on the Fab-side of the pill (Fab area + gap). */
const TRIGGER_RESERVE = `${FAB_PX + GAP_PX}px`

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
  contentShape = 'pill',
}: CircularActionProps) {
  const triggerRef = useRef<HTMLButtonElement>(null)
  const contentId = `${id}-content`

  const isHorizontal = expandDirection === 'left' || expandDirection === 'right'
  const collapseOrientation: 'horizontal' | 'vertical' = isHorizontal ? 'horizontal' : 'vertical'
  const isPill = contentShape === 'pill'

  // ── Overlay position ─────────────────────────────────────────────────────────
  // pill  → anchor to the Fab's same edge so the pill's rounded end aligns with the
  //         Fab's circular border; the Fab sits INSIDE the pill (pill wraps it).
  // card  → position cleanly below (or beside) the Fab — no overlap with the Fab.
  const overlayPositionSx = isPill
    ? (expandDirection === 'left'  ? { top: 0, right: 0 }       :
       expandDirection === 'right' ? { top: 0, left: 0 }        :
       expandDirection === 'up'    ? { bottom: 0, right: 0 }    :
                                     { top: 0, right: 0 })       // 'down'
    : (expandDirection === 'left'  ? { top: '100%', right: 0, mt: 0.5 }  :
       expandDirection === 'right' ? { top: '100%', left: 0,  mt: 0.5 }  :
       expandDirection === 'up'    ? { bottom: '100%', right: 0, mb: 0.5 } :
                                     { top: '100%',  right: 0, mt: 0.5 }) // 'down'

  // ── Content padding ──────────────────────────────────────────────────────────
  // pill  → reserve space on the Fab side (FAB_PX + GAP_PX) so the Fab appears
  //         embedded inside the pill while still being the topmost interactive element.
  // card  → zero padding (the card's content manages its own internal spacing).
  const contentPadding = isPill
    ? (expandDirection === 'left'  ? { pl: 1.5, pr: TRIGGER_RESERVE, py: 0 }  :
       expandDirection === 'right' ? { pr: 1.5, pl: TRIGGER_RESERVE, py: 0 }  :
       expandDirection === 'up'    ? { pt: 1.5, pb: TRIGGER_RESERVE, px: 0 }  :
                                     { pb: 1.5, pt: TRIGGER_RESERVE, px: 0 }) // 'down'
    : { p: 0 }

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && expanded) {
      event.stopPropagation()
      onToggle()
      triggerRef.current?.focus()
    }
  }

  return (
    <ClickAwayListener onClickAway={() => expanded && onToggle()}>
      {/* Outer Box sets the in-flow footprint (Fab size only — never changes on expand). */}
      <Box onKeyDown={handleKeyDown} sx={{ position: 'relative', display: 'inline-flex' }}>

          {/* ── Trigger Fab ─────────────────────────────────────────────────────────
            Rendered FIRST in DOM so it appears first in tab order: focus on the Fab
            → Tab → expanded options (next in DOM). z-index:2 keeps it painted above
            the overlay (z-index:1) even though the overlay follows in DOM order.
            `size="small"` keeps the trigger the same 40 px as the option IconButtons. */}
        <Box sx={{ position: 'relative', zIndex: 2 }}>
          <Badge
            color="secondary"
            variant="dot"
            overlap="circular"
            invisible={!badge}
          >
            <Fab
              ref={triggerRef}
              size="small"
              aria-label={label}
              aria-expanded={expanded}
              aria-controls={contentId}
              onClick={onToggle}
              disabled={disabled}
              sx={{
                width: FAB_PX,
                height: FAB_PX,
                minHeight: FAB_PX,
                boxShadow: '0 2px 8px rgba(0,0,0,0.28)',
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
                transition: (t) =>
                  t.transitions.create(['transform', 'background-color', 'color', 'box-shadow']),
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
        </Box>

        {/* ── Content overlay ────────────────────────────────────────────────────
            Rendered AFTER the Fab so tab order is Fab → options (DOM order = tab order).
            z-index:1 keeps it below the Fab (z-index:2) so the Fab stays clickable
            even when the pill background covers the Fab's area. */}
        <Box
          sx={{
            position: 'absolute',
            zIndex: 1,
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
                display: 'flex',
                alignItems: isPill ? 'center' : 'stretch',
                flexDirection: isHorizontal ? 'row' : 'column',
                overflow: 'hidden',
                bgcolor: (t) => t.palette.mode === 'dark'
                  ? 'oklch(0.18 0.02 280 / 0.97)'
                  : 'rgba(255,255,255,0.96)',
                border: (t) => t.palette.mode === 'dark'
                  ? '1px solid oklch(0.34 0.02 280 / 0.6)'
                  : '1px solid rgba(0,0,0,0.12)',
                borderRadius: isPill ? `${radius.pill}px` : '12px',
                backdropFilter: 'blur(12px)',
                boxShadow: '0 4px 16px rgba(0,0,0,0.28)',
                color: (t) => t.palette.mode === 'dark'
                  ? 'oklch(0.97 0.01 100)'
                  : 'rgba(0,0,0,0.87)',
                whiteSpace: isPill ? 'nowrap' : undefined,
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
