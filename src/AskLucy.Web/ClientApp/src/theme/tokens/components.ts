import { alpha } from '@mui/material/styles'
import type { Components, Theme } from '@mui/material/styles'
import { radius } from './palette'

export function createComponents(): Components<Theme> {
  return {
    MuiCssBaseline: {
      styleOverrides: (theme) => {
        const thumb = alpha(theme.palette.text.primary, 0.18)
        const thumbHover = alpha(theme.palette.text.primary, 0.32)

        return {
          body: {
            scrollbarWidth: 'thin',
            scrollbarColor: `${thumb} transparent`,
          },
          // Every scrollable surface (chat history, message list, etc.) gets the same
          // quiet, theme-matched scrollbar instead of the browser's default — Chromium
          // ignores `scrollbar-color` above, so it needs the ::-webkit-scrollbar family.
          '*::-webkit-scrollbar': {
            width: 8,
            height: 8,
          },
          '*::-webkit-scrollbar-track': {
            background: 'transparent',
          },
          '*::-webkit-scrollbar-thumb': {
            backgroundColor: thumb,
            borderRadius: radius.pill,
            border: '2px solid transparent',
            backgroundClip: 'padding-box',
          },
          '*::-webkit-scrollbar-thumb:hover': {
            backgroundColor: thumbHover,
          },
        }
      },
    },
    MuiButton: {
      defaultProps: { disableElevation: true },
      styleOverrides: {
        root: { borderRadius: radius.sm, paddingInline: 16 },
        sizeLarge: { paddingBlock: 10 },
      },
    },
    MuiPaper: {
      styleOverrides: {
        root: { backgroundImage: 'none' },
        rounded: { borderRadius: radius.lg },
      },
    },
    MuiCard: {
      styleOverrides: {
        root: { borderRadius: radius.lg },
      },
    },
    MuiOutlinedInput: {
      styleOverrides: {
        root: { borderRadius: radius.sm },
      },
    },
    MuiAppBar: {
      styleOverrides: {
        root: ({ theme }) => ({
          borderBottom: `1px solid ${theme.palette.divider}`,
        }),
      },
    },
    MuiChip: {
      styleOverrides: {
        root: { borderRadius: radius.pill },
      },
    },
    MuiDialog: {
      styleOverrides: {
        paper: { borderRadius: radius.lg },
      },
    },
    MuiTooltip: {
      styleOverrides: {
        tooltip: { borderRadius: radius.xs },
      },
    },
    MuiSkeleton: {
      styleOverrides: {
        root: { borderRadius: radius.sm },
      },
    },
    MuiLinearProgress: {
      styleOverrides: {
        root: { borderRadius: radius.pill, height: 6 },
      },
    },
    MuiCircularProgress: {
      defaultProps: { thickness: 3.6 },
    },
    MuiAlert: {
      styleOverrides: {
        root: { borderRadius: radius.md },
      },
    },
  }
}
