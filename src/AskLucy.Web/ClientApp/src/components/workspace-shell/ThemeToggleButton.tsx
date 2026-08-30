import { RiContrast2Line } from '@remixicon/react'
import { Fab } from '@mui/material'
import { useThemeStore } from '../../store/themeStore'
import { CIRCULAR_ACTION_CHROME } from './CircularAction'

/** A direct-action circular button (readdy.ai reference: the sun/moon icon beside the
 * account avatar) — toggles immediately on click, no expand/collapse state, so it isn't
 * a `CircularAction` (that component's whole purpose is the disclosure/expand pattern).
 * Shares `CircularAction`'s collapsed chrome so it reads as part of the same control
 * family sitting right next to the account control. */
export function ThemeToggleButton() {
  const toggle = useThemeStore((s) => s.toggle)

  return (
    <Fab
      size="small"
      aria-label="Toggle theme"
      onClick={toggle}
      sx={{
        // 40 px, matching CircularAction's trigger Fab (FAB_PX). MUI's `medium` Fab is 48 px,
        // which left the account button visibly smaller than the two buttons beside it.
        width: 40,
        height: 40,
        minHeight: 40,
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
        bgcolor: CIRCULAR_ACTION_CHROME.collapsedBg,
        color: CIRCULAR_ACTION_CHROME.icon,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        '&:hover': { bgcolor: CIRCULAR_ACTION_CHROME.collapsedHoverBg, transform: 'scale(1.05)' },
        transition: (t) => t.transitions.create(['transform', 'background-color']),
      }}
    >
      <RiContrast2Line />
    </Fab>
  )
}
