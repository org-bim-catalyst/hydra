import { alpha, darken, lighten } from '@mui/material'
import type { SxProps, Theme } from '@mui/material'

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
 *
 * Kept in its own module (not exported alongside the `CircularAction` component) so Fast
 * Refresh can treat `CircularAction.tsx` as a component-only file.
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

/**
 * The one-tap 40 px circular button every floating workspace control shares — Home, the theme
 * toggle, and the viewer's own toolbar entries (Arrange panels, Solar Analysis). 40 px matches
 * `CircularAction`'s trigger Fab (FAB_PX); MUI's `medium` Fab is 48 px. One definition so the
 * whole family changes together, in both themes.
 */
export const CIRCULAR_BUTTON_SX = {
  width: 40,
  height: 40,
  minHeight: 40,
  boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
  bgcolor: CIRCULAR_ACTION_CHROME.collapsedBg,
  color: CIRCULAR_ACTION_CHROME.icon,
  border: CIRCULAR_ACTION_CHROME.border,
  backdropFilter: 'blur(12px)',
  '&:hover': { bgcolor: CIRCULAR_ACTION_CHROME.collapsedHoverBg, transform: 'scale(1.05)' },
  transition: (t: Theme) => t.transitions.create(['transform', 'background-color']),
} satisfies SxProps<Theme>
