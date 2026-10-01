import { RiPushpin2Fill, RiPushpin2Line } from '@remixicon/react'
import { Badge, Box, ClickAwayListener, Fab, IconButton, type Theme } from '@mui/material'
import { type KeyboardEvent, type ReactNode, useRef } from 'react'
import { radius } from '../../theme'
import { zIndex } from '../../theme/tokens/zIndex'
import { CIRCULAR_ACTION_CHROME } from './circularActionChrome'

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
  /** Pinned ribbons ignore outside clicks (the parent must also keep `expanded` true). Only
   * meaningful for the 'pill' shape. */
  pinned?: boolean
  /** Supplying this renders the pin button at the ribbon's far end (pill shape only). */
  onTogglePin?: () => void
}

/** FAB_PX: the trigger Fab's width/height (40 px, same as option IconButtons so all
 * items in the ribbon are the same visual size). Used to calculate pill padding. */
const FAB_PX = 40
/** GAP_PX: gap inside the pill between the last option icon and the Fab's edge. */
const GAP_PX = 8
/** TRIGGER_RESERVE: total padding on the Fab-side of the pill (Fab area + gap). */
const TRIGGER_OVERLAP_PX = 10
/** The pill's Fab-side edge sits this far inside the Fab's outer edge, so the ribbon's rounded
 * cap tucks under the Fab (z-index 1 vs 2) instead of ending flush with it. */
const TRIGGER_RESERVE = `${FAB_PX + GAP_PX - TRIGGER_OVERLAP_PX}px`
/** PIN_PX: the pin badge — about half the size of the option buttons. */
const PIN_PX = 24
/** PIN_OVERHANG_PX: how far the pin hangs outside the ribbon's far tip (the rest overlaps it). */
const PIN_OVERHANG_PX = 8
/** Far-end padding when the pin is shown: the part of the pin inside the pill plus a 4 px gap. */
const PIN_FAR_PAD = `${PIN_PX - PIN_OVERHANG_PX + 4}px`

const pinRed = (t: Theme) => (t.palette.mode === 'dark' ? '#c34e4e' : 'red')
const pinSurface = (t: Theme) => (t.palette.mode === 'dark' ? '#11121c' : '#fefefe')

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
  pinned = false,
  onTogglePin,
}: CircularActionProps) {
  const triggerRef = useRef<HTMLButtonElement>(null)
  const contentId = `${id}-content`

  const isHorizontal = expandDirection === 'left' || expandDirection === 'right'
  const isPill = contentShape === 'pill'
  const showPin = isPill && onTogglePin !== undefined

  // ── Overlay position ─────────────────────────────────────────────────────────
  // pill  → anchored at the Fab's same edge (pill wraps the Fab); the Fab sits INSIDE
  //         the pill at z-index:2 so it stays interactive and its circular border aligns
  //         with the pill's rounded end.
  // card  → positioned cleanly below (or beside) the Fab — no Fab overlap.
  // GAP_PX offsets on the cross-axis center the pill around the Fab.
  // Without the offset the pill starts at the Fab's edge and extends entirely beyond it,
  // so the Fab sits at the pill's edge rather than its visual center.
  const overlayPositionSx = isPill
    ? (expandDirection === 'left'  ? { top: `-${GAP_PX}px`,    right: `${TRIGGER_OVERLAP_PX}px` } :
       expandDirection === 'right' ? { top: `-${GAP_PX}px`,    left: `${TRIGGER_OVERLAP_PX}px` }  :
       expandDirection === 'up'    ? { bottom: `${TRIGGER_OVERLAP_PX}px`, left: `-${GAP_PX}px` }  :
                                     { top: `${TRIGGER_OVERLAP_PX}px`,   left: `-${GAP_PX}px` }) // 'down'
    : (expandDirection === 'left'  ? { top: '100%', right: 0, mt: 0.5 }  :
       expandDirection === 'right' ? { top: '100%', left: 0,  mt: 0.5 }  :
       expandDirection === 'up'    ? { bottom: '100%', right: 0, mb: 0.5 } :
                                     { top: '100%',  right: 0, mt: 0.5 }) // 'down'

  // ── Content padding ──────────────────────────────────────────────────────────
  // pill  → Fab side reserves FAB_PX + GAP_PX − TRIGGER_OVERLAP_PX (38 px, the pill's edge
  //         starts 10 px inside the Fab's outer edge); far side = GAP_PX (8 px) and
  //         cross-axis = GAP_PX (8 px) → all visible gaps are equal at 8 px.
  // card  → 14 px uniform padding (per user requirement).
  const farPad = showPin ? PIN_FAR_PAD : 1
  const contentPadding = isPill
    ? (expandDirection === 'left'  ? { pl: farPad, pr: TRIGGER_RESERVE, py: 1 }  :
       expandDirection === 'right' ? { pr: farPad, pl: TRIGGER_RESERVE, py: 1 }  :
       expandDirection === 'up'    ? { pt: farPad, pb: TRIGGER_RESERVE, px: 1 }  :
                                     { pb: farPad, pt: TRIGGER_RESERVE, px: 1 }) // 'down'
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

  // A red badge straddling the ribbon's far tip, deliberately unlike the grey option buttons.
  // It is a SIBLING of the clipped content box, not a child: the box's clip-path would cut off
  // the half that hangs outside the pill. It reveals and hides with the same timing instead.
  const pinTipSx =
    expandDirection === 'left'  ? { left: `-${PIN_OVERHANG_PX}px`,   top: '50%', transform: 'translateY(-50%)' } :
    expandDirection === 'right' ? { right: `-${PIN_OVERHANG_PX}px`,  top: '50%', transform: 'translateY(-50%)' } :
    expandDirection === 'up'    ? { top: `-${PIN_OVERHANG_PX}px`,    left: '50%', transform: 'translateX(-50%)' } :
                                  { bottom: `-${PIN_OVERHANG_PX}px`, left: '50%', transform: 'translateX(-50%)' }
  const pinButton = showPin && (
    <IconButton
      onClick={onTogglePin}
      inert={!expanded}
      aria-label={pinned ? `Unpin ${label}` : `Pin ${label}`}
      aria-pressed={pinned}
      title={pinned ? 'Unpin' : 'Pin open'}
      sx={{
        position: 'absolute',
        ...pinTipSx,
        width: PIN_PX,
        height: PIN_PX,
        p: 0,
        // Unpinned: a flat badge in the ribbon's own border colour with a red pin. Pinned: the
        // colours swap, so the state reads at a glance.
        color: (t) => pinned ? pinSurface(t) : pinRed(t),
        bgcolor: (t) => pinned ? pinRed(t) : pinSurface(t),
        border: (t) => `1px solid ${t.palette.mode === 'dark' ? 'oklch(0.34 0.02 280 / 0.6)' : 'rgba(0,0,0,0.12)'}`,
        opacity: expanded ? 1 : 0,
        visibility: expanded ? 'visible' : 'hidden',
        transition: expanded
          ? 'opacity 220ms, background-color 120ms, visibility 0s 0ms'
          : 'opacity 220ms, background-color 120ms, visibility 0s 220ms',
        '&:hover': { bgcolor: pinRed, color: pinSurface },
        // Unpinned the pin leans like a pin lying on its side; pinned it is driven in upright.
        '& svg': { transform: pinned ? 'none' : 'rotate(45deg)', transition: 'transform 120ms' },
      }}
    >
      {pinned ? <RiPushpin2Fill size={14} /> : <RiPushpin2Line size={14} />}
    </IconButton>
  )

  const handleKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key === 'Escape' && expanded) {
      event.stopPropagation()
      onToggle()
      triggerRef.current?.focus()
    }
  }

  return (
    <ClickAwayListener onClickAway={() => expanded && !pinned && onToggle()}>
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
          {pinButton}
        </Box>
      </Box>
    </ClickAwayListener>
  )
}
