import { detectBrowserEngine } from './detectBrowserEngine'
import { voicePersonaMap, type BrowserEngine } from './voicePersonaMap'

export interface SelectedVoiceResult {
  voice: SpeechSynthesisVoice | null
  source: 'curated' | 'heuristic' | 'none'
}

/** Given names (plus the generic "female") that identify a female voice in the catalogs the
 * major vendors ship (research.md §3): Google's Chrome voices, Windows SAPI, Edge's online
 * "(Natural)" voices and macOS/iOS. Lowercase; matched against whole words of the voice
 * name, so "aria" can't match inside "Bulgaria". Used only by the heuristic tier. */
const FEMALE_NAME_TOKENS = new Set([
  'female',
  // English
  'zira', 'hazel', 'susan', 'heera', 'aria', 'jenny', 'michelle', 'emma', 'ava', 'sonia', 'libby',
  'natasha', 'clara', 'neerja', 'samantha', 'karen', 'moira', 'tessa', 'fiona', 'victoria',
  'allison', 'serena', 'veena', 'kate', 'salli', 'joanna',
  // Arabic
  'hoda', 'zariyah', 'salma', 'fatima', 'amany', 'laila', 'mouna', 'sana', 'noura', 'amina',
  // Spanish
  'helena', 'laura', 'sabina', 'elvira', 'dalia', 'paloma', 'monica', 'mónica', 'paulina',
  // French
  'hortense', 'julie', 'denise', 'eloise', 'sylvie', 'amelie', 'amélie', 'audrey', 'aurelie', 'aurélie',
  // German
  'hedda', 'katja', 'amala', 'ingrid', 'anna', 'petra',
])

/** Given names (plus "male") of the male voices in the same catalogs. A known-male voice
 * ranks below one whose gender the name doesn't reveal, so it's only chosen when it is the
 * sole voice for the language — speaking in it still beats not speaking at all. */
const MALE_NAME_TOKENS = new Set([
  'male',
  'david', 'mark', 'george', 'ravi', 'guy', 'ryan', 'christopher', 'eric', 'roger', 'steffan',
  'andrew', 'brian', 'william', 'daniel', 'alex', 'fred', 'tom', 'oliver', 'thomas', 'rishi',
  'naayf', 'hamed', 'shakir', 'majed', 'maged', 'tarik', 'rami', 'hamdan',
  'pablo', 'raul', 'raúl', 'alvaro', 'álvaro', 'jorge', 'juan', 'diego',
  'paul', 'claude', 'henri', 'jerome', 'jérôme', 'thierry', 'nicolas',
  'stefan', 'conrad', 'killian', 'markus', 'yannick',
])

function scoreVoice(voice: SpeechSynthesisVoice): number {
  const words = voice.name.toLowerCase().split(/[^\p{L}]+/u)
  let score = 0
  if (words.some((word) => FEMALE_NAME_TOKENS.has(word))) score += 10
  else if (words.some((word) => MALE_NAME_TOKENS.has(word))) score -= 10
  // Edge's neural voices sound markedly closer to a young adult than the older SAPI ones.
  if (words.includes('natural')) score += 2
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
