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

/**
 * specs/068 US3 (FR-023/FR-024) - what voice says instead of reciting an offer card.
 *
 * A card is a form. Read aloud in full - question, then every label, then every description,
 * then the confirm action - it is a long recital of text the listener cannot act on by ear
 * anyway. The cue tells them an offer is waiting and leaves the reading to the screen, which is
 * the only part of it that is actually selectable. Nothing here changes the voice persona
 * (CLAUDE.md, User Experience): this governs only what is spoken, never who speaks it.
 *
 * FR-025 - suppressing this from speech takes nothing away from assistive technology: the card's
 * full question, options and descriptions remain in the accessibility tree.
 */
export const OFFER_VOICE_CUES: Record<string, string> = {
  en: 'There are a few options below to choose from.',
  ar: 'هناك بعض الخيارات أدناه للاختيار منها.',
  es: 'Abajo tienes algunas opciones para elegir.',
  fr: 'Quelques options sont proposées ci-dessous.',
  de: 'Unten stehen einige Optionen zur Auswahl.',
}

/** The cue for a language, falling back to English rather than to silence: a listener who is
 * never told the offer exists has no way to discover it by ear. Matches on the base subtag, so
 * `en-GB` and `ar-AE` resolve rather than falling through. */
export function offerVoiceCue(language: string): string {
  const base = language.split('-')[0].toLowerCase()
  return OFFER_VOICE_CUES[base] ?? OFFER_VOICE_CUES.en
}
