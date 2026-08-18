import { createTheme } from '@mui/material/styles'
import type { ThemeMode } from '../store/themeStore'
import { createComponents } from './tokens/components'
import { createMotionTokens } from './tokens/motion'
import { createPalette, opacity, radius } from './tokens/palette'
import { createShadows } from './tokens/shadows'
import { typography } from './tokens/typography'
import { zIndex } from './tokens/zIndex'

export { opacity, radius }

/** `prefersReducedMotion` collapses MUI's own Dialog/Drawer/Menu/Collapse transition
 * durations to 0 (FR-010) — see `theme/tokens/motion.ts` and `hooks/usePrefersReducedMotion`. */
export function createAppTheme(mode: ThemeMode, prefersReducedMotion = false) {
  const isDark = mode === 'dark'
  const motion = createMotionTokens(prefersReducedMotion)

  const theme = createTheme({
    palette: createPalette(mode),
    typography,
    shape: { borderRadius: radius.sm },
    shadows: createShadows(isDark),
    components: createComponents(),
    zIndex,
    transitions: {
      duration: {
        shortest: motion.duration.fast,
        shorter: motion.duration.fast,
        short: motion.duration.standard,
        standard: motion.duration.standard,
        complex: motion.duration.slow,
        enteringScreen: motion.duration.standard,
        leavingScreen: motion.duration.fast,
      },
      easing: {
        easeInOut: motion.easing.standard,
        easeOut: motion.easing.decelerate,
        easeIn: motion.easing.accelerate,
        sharp: motion.easing.standard,
      },
    },
  })

  return theme
}
