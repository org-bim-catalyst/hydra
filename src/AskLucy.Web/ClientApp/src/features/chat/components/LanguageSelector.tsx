import { MenuItem, TextField } from '@mui/material'

const LANGUAGES = [
  { code: 'en', label: 'English' },
  { code: 'ar', label: 'Arabic' },
  { code: 'es', label: 'Spanish' },
  { code: 'fr', label: 'French' },
  { code: 'de', label: 'German' },
]

interface LanguageSelectorProps {
  value: string
  onChange: (code: string) => void
}

/** FR-013: target-language selector driving AI-assisted translation. */
export function LanguageSelector({ value, onChange }: LanguageSelectorProps) {
  return (
    <TextField select size="small" label="Language" value={value} onChange={(e) => onChange(e.target.value)} sx={{ minWidth: 140 }}>
      {LANGUAGES.map((lang) => (
        <MenuItem key={lang.code} value={lang.code}>
          {lang.label}
        </MenuItem>
      ))}
    </TextField>
  )
}
