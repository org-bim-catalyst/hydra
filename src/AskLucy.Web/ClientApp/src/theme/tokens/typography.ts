import type { TypographyVariantsOptions } from '@mui/material/styles'

export const fontFamily = ['Inter', 'Segoe UI', 'Roboto', 'Arial', 'sans-serif'].join(',')
export const codeFontFamily = ['JetBrains Mono', 'Cascadia Code', 'Consolas', 'monospace'].join(',')

export const typography: TypographyVariantsOptions = {
  fontFamily,
  h1: { fontWeight: 700, fontSize: '2.75rem', lineHeight: 1.2, letterSpacing: '-0.02em' },
  h2: { fontWeight: 700, fontSize: '2.25rem', lineHeight: 1.25, letterSpacing: '-0.02em' },
  h3: { fontWeight: 600, fontSize: '1.75rem', lineHeight: 1.3 },
  h4: { fontWeight: 600, fontSize: '1.5rem', lineHeight: 1.3 },
  h5: { fontWeight: 600, fontSize: '1.25rem', lineHeight: 1.35 },
  h6: { fontWeight: 600, fontSize: '1.0625rem', lineHeight: 1.4 },
  subtitle1: { fontWeight: 500, fontSize: '1rem', lineHeight: 1.5 },
  subtitle2: { fontWeight: 500, fontSize: '0.875rem', lineHeight: 1.5 },
  body1: { fontWeight: 400, fontSize: '1rem', lineHeight: 1.55 },
  body2: { fontWeight: 400, fontSize: '0.875rem', lineHeight: 1.55 },
  button: { fontWeight: 600, textTransform: 'none' },
  caption: { fontWeight: 400, fontSize: '0.75rem', lineHeight: 1.5 },
  overline: { fontWeight: 600, fontSize: '0.6875rem', letterSpacing: '0.08em' },
}
