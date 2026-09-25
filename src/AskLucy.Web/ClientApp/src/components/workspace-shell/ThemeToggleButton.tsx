import { RiMoonLine, RiSunLine } from '@remixicon/react'
import { Fab } from '@mui/material'
import { useThemeStore } from '../../store/themeStore'
import { CIRCULAR_BUTTON_SX } from './circularActionChrome'

/** A direct-action circular button (readdy.ai reference: the sun/moon icon beside the
 * account avatar) — toggles immediately on click, no expand/collapse state, so it isn't
 * a `CircularAction` (that component's whole purpose is the disclosure/expand pattern).
 * Shares `CircularAction`'s collapsed chrome so it reads as part of the same control
 * family sitting right next to the account control. */
export function ThemeToggleButton() {
  const toggle = useThemeStore((s) => s.toggle)
  const mode = useThemeStore((s) => s.mode)
  const isDark = mode === 'dark'

  return (
    <Fab
      size="small"
      // The reference names the destination, not the current state: `aria-label="Toggle dark
      // mode"` with `${isDark ? 'ri-sun-line' : 'ri-moon-line'}`. A fixed contrast glyph — what
      // this was — tells you nothing about what pressing it will do.
      aria-label={isDark ? 'Switch to light mode' : 'Switch to dark mode'}
      onClick={toggle}
      sx={CIRCULAR_BUTTON_SX}
    >
      {isDark ? <RiSunLine size={20} /> : <RiMoonLine size={20} />}
    </Fab>
  )
}
