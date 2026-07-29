import type { PaletteOptions } from '@mui/material/styles'
import type { ThemeMode } from '../../store/themeStore'

export const radius = {
  xs: 4,
  sm: 8,
  md: 12,
  lg: 16,
  xl: 24,
  pill: 999,
} as const

const gray = {
  50: '#F9FAFB',
  100: '#F3F4F6',
  200: '#E5E7EB',
  300: '#D1D5DB',
  400: '#9CA3AF',
  500: '#6B7280',
  600: '#4B5563',
  700: '#374151',
  800: '#1F2937',
  900: '#111827',
}

export function createPalette(mode: ThemeMode): PaletteOptions {
  const isDark = mode === 'dark'

  return {
    mode,
    primary: {
      main: '#4F46E5',
      light: '#818CF8',
      dark: '#3730A3',
      contrastText: '#FFFFFF',
    },
    secondary: {
      main: '#0D9488',
      light: '#5EEAD4',
      dark: '#115E59',
      contrastText: '#FFFFFF',
    },
    success: { main: '#16A34A' },
    warning: { main: '#F59E0B' },
    error: { main: '#DC2626' },
    info: { main: '#0EA5E9' },
    grey: gray,
    background: {
      default: isDark ? '#0E1015' : gray[50],
      paper: isDark ? '#171922' : '#FFFFFF',
    },
    text: {
      primary: isDark ? gray[100] : gray[900],
      secondary: isDark ? gray[400] : gray[600],
    },
    divider: isDark ? 'rgba(255, 255, 255, 0.08)' : gray[200],
  }
}
