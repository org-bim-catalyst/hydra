export interface LanguageOption {
  code: string
  label: string
}

/** specs/026-floating-chat-assistant research.md #6 — shared across `ActiveLanguageFlag`
 * and Chat Configuration's default-language control; extracted from the removed
 * `LanguageSelector.tsx`'s original list so both consumers stay in sync. */
export const SUPPORTED_LANGUAGES: LanguageOption[] = [
  { code: 'en', label: 'English' },
  { code: 'ar', label: 'Arabic' },
  { code: 'es', label: 'Spanish' },
  { code: 'fr', label: 'French' },
  { code: 'de', label: 'German' },
]

/** Circular flag glyph per language code (research.md #6) — a code with no entry falls
 * back to a generic globe glyph rather than rendering nothing. */
export const LANGUAGE_FLAGS: Record<string, string> = {
  en: '🇬🇧',
  ar: '🇸🇦',
  es: '🇪🇸',
  fr: '🇫🇷',
  de: '🇩🇪',
}

export const DEFAULT_LANGUAGE_FLAG = '🌐'
