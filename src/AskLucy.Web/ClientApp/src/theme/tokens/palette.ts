import type { PaletteOptions } from '@mui/material/styles'
import type { ThemeMode } from '../../store/themeStore'

export const radius = {
  xs: 3,
  sm: 6,
  md: 10,
  lg: 14,
  xl: 20,
  pill: 999,
} as const

// "Drafting table" neutrals: warm vellum paper / graphite ink, rather than the
// clinical blue-gray gray-scale most AI-product UIs default to.
const graphite = {
  50: '#F7F6F2',
  100: '#EEECE5',
  200: '#DEDBD1',
  300: '#C3BFB1',
  400: '#9A9587',
  500: '#726D62',
  600: '#524E46',
  700: '#3B3833',
  800: '#26241F',
  900: '#171613',
}

export function createPalette(mode: ThemeMode): PaletteOptions {
  const isDark = mode === 'dark'

  return {
    mode,
    // "Pen" — a desaturated technical-ink blue, standing in for the indigo/purple
    // every AI product defaults to. Reads as precise and considered, not playful.
    primary: {
      main: '#1F4E5E',
      light: '#4C7B8B',
      dark: '#123340',
      contrastText: '#F7F6F2',
    },
    // "Redline" — the mark-up red an architect or engineer reaches for on a
    // drawing review. Spent sparingly: the one deliberate accent, never on
    // large surfaces, and never used where it could be mistaken for an error.
    secondary: {
      main: '#B8461F',
      light: '#D97650',
      dark: '#7E2E12',
      contrastText: '#F7F6F2',
    },
    success: { main: '#3F7D4E' },
    warning: { main: '#B8791F' },
    error: { main: '#B23B2E' },
    info: { main: '#1F4E5E' },
    grey: graphite,
    background: {
      default: isDark ? '#14130F' : graphite[50],
      paper: isDark ? '#1D1B17' : '#FFFFFF',
    },
    text: {
      primary: isDark ? graphite[100] : graphite[900],
      secondary: isDark ? graphite[400] : graphite[600],
    },
    divider: isDark ? 'rgba(247, 246, 242, 0.1)' : graphite[200],
  }
}
