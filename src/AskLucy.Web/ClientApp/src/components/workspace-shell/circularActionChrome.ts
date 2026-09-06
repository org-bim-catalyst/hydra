import { alpha, darken, lighten } from '@mui/material'
import type { Theme } from '@mui/material'

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
