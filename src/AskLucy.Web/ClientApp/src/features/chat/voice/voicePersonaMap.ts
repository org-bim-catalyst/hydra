export type LanguageCode = 'en' | 'ar' | 'es' | 'fr' | 'de'
export type BrowserEngine = 'chromium' | 'firefox' | 'webkit'

/** Ordered candidate voice names for one (language, engine) pair — the first name
 * present in speechSynthesis.getVoices() wins (contracts/voice-persona-mapping.md).
 * An absent (language, engine) entry means "not yet curated" and falls through to
 * selectPersonaVoice's heuristic tier — it is never represented as an empty array. */
export type VoicePersonaMap = Partial<Record<LanguageCode, Partial<Record<BrowserEngine, string[]>>>>

/**
 * Curated, best-effort young-adult-female voice names per (language, browser engine),
 * sourced from publicly documented Chrome/Edge "Google …" network voices, Windows SAPI
 * desktop voices, and macOS/iOS AVSpeechSynthesis voices.
 *
 * NOT YET VERIFIED against real installed voice catalogs on real devices — research.md
 * §3 / tasks.md T009 requires a manual cross-browser/OS audit before this is treated as
 * final; exact availability varies by OS version. Entries deliberately omitted (rather
 * than guessed) where no confidently-known female voice name exists — those combinations
 * correctly fall through to selectPersonaVoice's scored heuristic instead of risking a
 * wrong (e.g. male) name being hardcoded into a persona map that must be female by
 * definition (constitution §7 Voice output).
 */
export const voicePersonaMap: VoicePersonaMap = {
  en: {
    chromium: ['Google UK English Female', 'Microsoft Zira Desktop', 'Microsoft Zira', 'Samantha'],
    firefox: ['Microsoft Zira Desktop', 'Samantha'],
    webkit: ['Samantha', 'Karen', 'Moira', 'Tessa'],
  },
  es: {
    chromium: ['Google español', 'Microsoft Helena Desktop', 'Mónica'],
    firefox: ['Microsoft Helena Desktop', 'Mónica'],
    webkit: ['Mónica', 'Paulina'],
  },
  fr: {
    chromium: ['Google français', 'Microsoft Hortense Desktop', 'Amélie'],
    firefox: ['Microsoft Hortense Desktop', 'Amélie'],
    webkit: ['Amélie', 'Audrey'],
  },
  de: {
    chromium: ['Google Deutsch', 'Microsoft Hedda Desktop', 'Anna'],
    firefox: ['Microsoft Hedda Desktop', 'Anna'],
    webkit: ['Anna', 'Petra'],
  },
  // ar: intentionally uncurated for now — no confidently-known, widely-shipped female
  // Arabic voice name across these engines as of this writing (T009 must confirm real
  // options during the manual audit). selectPersonaVoice's heuristic tier handles this
  // language today via its lang-match + name-token scoring, never an arbitrary voice.
}
