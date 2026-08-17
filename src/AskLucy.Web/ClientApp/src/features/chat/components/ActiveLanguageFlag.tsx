import { Box, Tooltip } from '@mui/material'
import { DEFAULT_LANGUAGE_FLAG, LANGUAGE_FLAGS, SUPPORTED_LANGUAGES } from '../languageOptions'

export interface ActiveLanguageFlagProps {
  language: string
}

/** FR-016: read-only circular flag glyph reflecting the current active response
 * language — the only way its value changes is `language` changing upstream (a save in
 * Chat Configuration, FR-017); this component has no `onChange` of its own. */
export function ActiveLanguageFlag({ language }: ActiveLanguageFlagProps) {
  const flag = LANGUAGE_FLAGS[language] ?? DEFAULT_LANGUAGE_FLAG
  const label = SUPPORTED_LANGUAGES.find((l) => l.code === language)?.label ?? 'Default language'

  return (
    <Tooltip title={`Response language: ${label}`}>
      <Box
        role="img"
        aria-label={`Response language: ${label}`}
        sx={{
          width: 24,
          height: 24,
          borderRadius: '50%',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: '0.9rem',
          lineHeight: 1,
          overflow: 'hidden',
          bgcolor: 'rgba(255,255,255,0.08)',
          flexShrink: 0,
        }}
      >
        {flag}
      </Box>
    </Tooltip>
  )
}
