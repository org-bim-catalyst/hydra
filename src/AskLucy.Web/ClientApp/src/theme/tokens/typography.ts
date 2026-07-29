import type { TypographyVariantsOptions } from '@mui/material/styles'

// Display face carries the brand's personality; body face stays a neutral,
// highly legible workhorse. Deliberately not the same family for both.
export const displayFontFamily = ['"Space Grotesk"', 'Segoe UI', 'Roboto', 'Arial', 'sans-serif'].join(',')
export const fontFamily = ['Inter', 'Segoe UI', 'Roboto', 'Arial', 'sans-serif'].join(',')
export const codeFontFamily = ['"JetBrains Mono"', 'Cascadia Code', 'Consolas', 'monospace'].join(',')

export const typography: TypographyVariantsOptions = {
  fontFamily,
  h1: { fontFamily: displayFontFamily, fontWeight: 600, fontSize: '2.75rem', lineHeight: 1.15, letterSpacing: '-0.01em' },
  h2: { fontFamily: displayFontFamily, fontWeight: 600, fontSize: '2.25rem', lineHeight: 1.2, letterSpacing: '-0.01em' },
  h3: { fontFamily: displayFontFamily, fontWeight: 600, fontSize: '1.75rem', lineHeight: 1.25 },
  h4: { fontFamily: displayFontFamily, fontWeight: 600, fontSize: '1.5rem', lineHeight: 1.3 },
  h5: { fontFamily: displayFontFamily, fontWeight: 500, fontSize: '1.25rem', lineHeight: 1.35 },
  h6: { fontFamily: displayFontFamily, fontWeight: 500, fontSize: '1.0625rem', lineHeight: 1.4 },
  subtitle1: { fontWeight: 500, fontSize: '1rem', lineHeight: 1.5 },
  subtitle2: { fontWeight: 500, fontSize: '0.875rem', lineHeight: 1.5 },
  body1: { fontWeight: 400, fontSize: '1rem', lineHeight: 1.55 },
  body2: { fontWeight: 400, fontSize: '0.875rem', lineHeight: 1.55 },
  button: { fontWeight: 600, textTransform: 'none' },
  caption: { fontWeight: 400, fontSize: '0.75rem', lineHeight: 1.5 },
  // Mono-set annotation style, echoing a dimension callout on a technical
  // drawing rather than a generic UI eyebrow label.
  overline: {
    fontFamily: codeFontFamily,
    fontWeight: 500,
    fontSize: '0.6875rem',
    letterSpacing: '0.08em',
    textTransform: 'uppercase',
  },
}
