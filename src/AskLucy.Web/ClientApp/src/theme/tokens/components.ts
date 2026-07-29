import type { Components, Theme } from '@mui/material/styles'
import { radius } from './palette'

export function createComponents(): Components<Theme> {
  return {
    MuiCssBaseline: {
      styleOverrides: {
        body: {
          scrollbarWidth: 'thin',
        },
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
  }
}
