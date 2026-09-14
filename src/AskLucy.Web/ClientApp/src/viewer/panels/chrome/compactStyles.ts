import { alpha, type Theme } from '@mui/material'
import type { SystemStyleObject } from '@mui/system'

/** The compact panel look, taken from the solar-analysis reference page (sunpath-osm-shadows-13):
 * 11px dim labels, 12.5px rows on hairline dividers, monospace values, amber highlights, and small
 * bordered controls. Colours derive from the theme so the look holds in light and dark alike; only
 * the amber accent is fixed, as it is in the reference. */
export const COMPACT_MONO_FONT = "'JetBrains Mono', 'SF Mono', 'Cascadia Code', Consolas, monospace"
export const COMPACT_ACCENT = '#e8a03d'

export const compactLabelSx: SystemStyleObject<Theme> = {
  fontSize: 11,
  fontWeight: 500,
  lineHeight: 1.4,
  color: 'text.secondary',
}

export const compactRowSx: SystemStyleObject<Theme> = {
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'baseline',
  gap: 2,
  py: 0.5,
  fontSize: 12.5,
  lineHeight: 1.5,
  borderBottom: '1px solid',
  borderColor: (theme) => alpha(theme.palette.text.primary, 0.08),
}

export const compactInputSx: SystemStyleObject<Theme> = {
  fontFamily: COMPACT_MONO_FONT,
  fontSize: 12.5,
  color: 'text.primary',
  bgcolor: (theme) => alpha(theme.palette.text.primary, 0.04),
  border: '1px solid',
  borderColor: 'divider',
  borderRadius: '5px',
  '& .MuiInputBase-input': { px: 1, py: 0.625, height: 'auto' },
  '&.Mui-focused': { borderColor: COMPACT_ACCENT },
}

export const compactButtonSx: SystemStyleObject<Theme> = {
  minWidth: 0,
  px: 1.25,
  py: 0.5,
  fontSize: 11.5,
  lineHeight: 1.4,
  textTransform: 'none',
  color: 'text.primary',
  borderColor: 'divider',
  borderRadius: '5px',
  '&:hover': { borderColor: alpha(COMPACT_ACCENT, 0.5), bgcolor: 'transparent' },
}

export const compactAlertSx: SystemStyleObject<Theme> = {
  py: 0,
  px: 1,
  fontSize: 11.5,
  '& .MuiAlert-icon': { py: 0.5, mr: 0.75, fontSize: 16 },
  '& .MuiAlert-message': { py: 0.5 },
}
