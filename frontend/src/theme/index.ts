import { createTheme } from '@mui/material/styles'
import type { ThemeMode } from '../store/themeStore'

export function createAppTheme(mode: ThemeMode) {
  return createTheme({
    palette: {
      mode,
      primary: { main: '#4F46E5' },
    },
    shape: { borderRadius: 8 },
  })
}
