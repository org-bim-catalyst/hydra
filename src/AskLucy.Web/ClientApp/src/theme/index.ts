import { createTheme } from '@mui/material/styles'
import type { ThemeMode } from '../store/themeStore'
import { createComponents } from './tokens/components'
import { createPalette, radius } from './tokens/palette'
import { createShadows } from './tokens/shadows'
import { typography } from './tokens/typography'

export { radius }

export function createAppTheme(mode: ThemeMode) {
  const isDark = mode === 'dark'

  const theme = createTheme({
    palette: createPalette(mode),
    typography,
    shape: { borderRadius: radius.sm },
    shadows: createShadows(isDark),
    components: createComponents(),
  })

  return theme
}
