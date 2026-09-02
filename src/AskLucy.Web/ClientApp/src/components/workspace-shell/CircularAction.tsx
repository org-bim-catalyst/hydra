import { Badge, Box, ClickAwayListener, Fab, alpha, darken, lighten } from '@mui/material'
import type { Theme } from '@mui/material'
import { type KeyboardEvent, type ReactNode, useRef } from 'react'
import { radius } from '../../theme'
import { zIndex } from '../../theme/tokens/zIndex'

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

/**
 * Shared chrome for the floating workspace controls.
 *
 * Every value is a theme callback rather than a literal, because these were originally
 * sampled from the readdy.ai reference back when that page was light-mode only — which froze
 * the workspace chrome dark. The theme toggle then changed the app around these controls but
 * never the controls themselves.
 *
 * The dark-mode reference shows the intent: its floating buttons are
 * `bg-background-50/90 border-background-200 hover:bg-background-100 text-foreground-700`, and
 * its ramps invert between modes (`background-50` is the lightest surface in light mode and
 * the darkest in dark mode). Mapping those roles onto the MUI palette reproduces that: one
 * definition, correct in both modes.
 *
 * Each value is consumed inside an `sx` prop, where MUI resolves a per-property callback.
 */
export const CIRCULAR_ACTION_CHROME = {
  /** `bg-background-50/90` — the resting surface. */
  collapsedBg: (t: Theme) => alpha(t.palette.background.paper, 0.9),
  /** `hover:bg-background-100` — one step away from the page, in whichever direction is legible. */
  collapsedHoverBg: (t: Theme) =>
    t.palette.mode === 'dark' ? lighten(t.palette.background.paper, 0.08) : darken(t.palette.background.paper, 0.05),
  /** The expanded panel behind a control's options. */
  expandedBg: (t: Theme) => alpha(t.palette.background.paper, 0.97),
  expandedTriggerBg: (t: Theme) => t.palette.primary.main,
  expandedTriggerHoverBg: (t: Theme) => t.palette.primary.dark,
  /** `text-foreground-700`. */
  icon: (t: Theme) => t.palette.text.primary,
  /** `border-background-200`. */
  border: (t: Theme) => `1px solid ${alpha(t.palette.divider, 0.6)}`,
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
  const isPill = contentShape === 'pill'

  // ── Overlay position ─────────────────────────────────────────────────────────
  // pill  → anchored at the Fab's same edge (pill wraps the Fab); the Fab sits INSIDE
  //         the pill at z-index:2 so it stays interactive and its circular border aligns
  //         with the pill's rounded end.
  // card  → positioned cleanly below (or beside) the Fab — no Fab overlap.
  // GAP_PX offsets on the cross-axis center the pill around the Fab.
  // Without the offset the pill starts at the Fab's edge and extends entirely beyond it,
  // so the Fab sits at the pill's edge rather than its visual center.
  const overlayPositionSx = isPill
    ? (expandDirection === 'left'  ? { top: `-${GAP_PX}px`,    right: 0 }       :
       expandDirection === 'right' ? { top: `-${GAP_PX}px`,    left: 0 }        :
       expandDirection === 'up'    ? { bottom: 0, left: `-${GAP_PX}px` }        :
                                     { top: 0,   left: `-${GAP_PX}px` })         // 'down'
    : (expandDirection === 'left'  ? { top: '100%', right: 0, mt: 0.5 }  :
       expandDirection === 'right' ? { top: '100%', left: 0,  mt: 0.5 }  :
       expandDirection === 'up'    ? { bottom: '100%', right: 0, mb: 0.5 } :
                                     { top: '100%',  right: 0, mt: 0.5 }) // 'down'

  // ── Content padding ──────────────────────────────────────────────────────────
  // pill  → Fab side reserves FAB_PX + GAP_PX (48 px); far side = GAP_PX (8 px) and
  //         cross-axis = GAP_PX (8 px) → all visible gaps are equal at 8 px.
  // card  → 14 px uniform padding (per user requirement).
  const contentPadding = isPill
    ? (expandDirection === 'left'  ? { pl: 1, pr: TRIGGER_RESERVE, py: 1 }  :
       expandDirection === 'right' ? { pr: 1, pl: TRIGGER_RESERVE, py: 1 }  :
       expandDirection === 'up'    ? { pt: 1, pb: TRIGGER_RESERVE, px: 1 }  :
                                     { pb: 1, pt: TRIGGER_RESERVE, px: 1 }) // 'down'
    : { p: '14px' }

  // ── clip-path animation ──────────────────────────────────────────────────────
  // Replaces MUI <Collapse> to fix two problems with the Collapse approach:
  //   1. Collapse's overflow:hidden creates visible black edges during the slide.
  //   2. Collapse grows/shrinks the container size, making content appear to slide
  //      toward the Fab rather than away from it.
  // clip-path keeps the element full-size at all times; the inset values animate to
  // reveal/hide the content from the Fab side outward (Fab-adjacent content stays
  // visible longest during collapse and appears first during expand).
  const clipR = isPill ? radius.pill : 12
  const collapsedClipPath =
    expandDirection === 'left'  ? `inset(0 0 0 100% round ${clipR}px)` :
    expandDirection === 'right' ? `inset(0 100% 0 0 round ${clipR}px)` :
    expandDirection === 'up'    ? `inset(100% 0 0 0 round ${clipR}px)` :
                                  `inset(0 0 100% 0 round ${clipR}px)`  // 'down'
  const expandedClipPath = `inset(0 0 0 0% round ${clipR}px)`

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
            Rendered AFTER the Fab (DOM order = tab order: Fab → options).
            pill: z-index:1 keeps it below the Fab (z-index:2) so the Fab stays
            clickable even when the pill background covers the Fab's area.
            card: floats the dropdown above every other workspace element. It has to clear
            MUI's own Fab layer (theme.zIndex.fab, 1050) — the account card is a sibling of
            the theme and rotation Fabs in the top cluster, and at the old z-index:100 they
            painted over its top edge regardless of DOM order. */}
        <Box
          sx={{
            position: 'absolute',
            zIndex: isPill ? 1 : zIndex.dropdown,
            pointerEvents: expanded ? 'auto' : 'none',
            ...overlayPositionSx,
          }}
        >
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
              clipPath: expanded ? expandedClipPath : collapsedClipPath,
              // visibility: jsdom applies this immediately (no CSS-transition simulation),
              // which keeps the content out of the tab sequence in tests. Real browsers
              // respect the delay: on expand it becomes visible at t=0 (so the clip-path
              // reveal is visible); on collapse it becomes hidden at t=220ms (after the
              // clip-path animation finishes, matching the animation duration).
              visibility: expanded ? ('visible' as const) : ('hidden' as const),
              transition: expanded
                ? 'clip-path 220ms cubic-bezier(0.4, 0, 0.2, 1), visibility 0s 0ms'
                : 'clip-path 220ms cubic-bezier(0.4, 0, 0.2, 1), visibility 0s 220ms',
            }}
          >
            {children}
          </Box>
        </Box>
      </Box>
    </ClickAwayListener>
  )
}
