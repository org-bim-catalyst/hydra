import { detectBrowserEngine } from './detectBrowserEngine'
import { voicePersonaMap, type BrowserEngine } from './voicePersonaMap'

export interface SelectedVoiceResult {
  voice: SpeechSynthesisVoice | null
  source: 'curated' | 'heuristic' | 'none'
}

/** Name fragments that reliably identify a female-presenting voice across the major
 * browser/OS voice catalogs (research.md §3) — used only as the heuristic tier's scoring
 * signal, never as a substitute for curation. Lowercase; matched as a substring. */
const FEMALE_NAME_TOKENS = [
  'female',
  'zira',
  'hazel',
  'samantha',
  'karen',
  'moira',
  'tessa',
  'susan',
  'monica',
  'mónica',
  'paulina',
  'amelie',
  'amélie',
  'audrey',
  'anna',
  'helena',
  'hortense',
  'hedda',
  'petra',
  'victoria',
  'fiona',
  'kate',
  'salli',
  'joanna',
]

function scoreVoice(voice: SpeechSynthesisVoice): number {
  const name = voice.name.toLowerCase()
  let score = 0
  if (FEMALE_NAME_TOKENS.some((token) => name.includes(token))) score += 10
  if (voice.localService) score += 1
  return score
}

/** Curated lookup + scored heuristic fallback (contracts/voice-persona-mapping.md,
 * research.md §3). Pure — no `speechSynthesis` calls, no I/O — so it's unit-testable
 * with hand-built `SpeechSynthesisVoice`-shaped fixtures. `engine` defaults to a live
 * `detectBrowserEngine()` read for production call sites; tests always pass it explicitly
 * to keep the function's own behavior deterministic. */
export function selectPersonaVoice(
  lang: string,
  voices: SpeechSynthesisVoice[],
  engine: BrowserEngine | 'unknown' = detectBrowserEngine(),
): SelectedVoiceResult {
  const curatedNames = engine !== 'unknown' ? voicePersonaMap[lang as keyof typeof voicePersonaMap]?.[engine] : undefined

  if (curatedNames) {
    for (const name of curatedNames) {
      const match = voices.find((v) => v.name === name)
      if (match) return { voice: match, source: 'curated' }
    }
  }

  const languageMatches = voices.filter((v) => v.lang.toLowerCase().startsWith(lang.toLowerCase()))
  if (languageMatches.length === 0) {
    // constitution §2.VIII / FR-004: never fall back to an unrelated-language voice.
    return { voice: null, source: 'none' }
  }

  const best = languageMatches
    .map((voice) => ({ voice, score: scoreVoice(voice) }))
    .sort((a, b) => b.score - a.score)[0]

  return { voice: best.voice, source: 'heuristic' }
}
