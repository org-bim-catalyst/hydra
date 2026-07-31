import { describe, expect, it } from 'vitest'
import { selectPersonaVoice } from './selectPersonaVoice'

function makeVoice(overrides: Partial<SpeechSynthesisVoice> & { name: string; lang: string }): SpeechSynthesisVoice {
  return {
    default: false,
    localService: true,
    voiceURI: overrides.name,
    ...overrides,
  } as SpeechSynthesisVoice
}

describe('selectPersonaVoice', () => {
  it('returns the curated voice when it is present in the browser voice list (curated hit)', () => {
    const voices = [
      makeVoice({ name: 'Microsoft David Desktop', lang: 'en-US' }),
      makeVoice({ name: 'Microsoft Zira Desktop', lang: 'en-US' }),
    ]

    const result = selectPersonaVoice('en', voices, 'chromium')

    expect(result).toEqual({ voice: voices[1], source: 'curated' })
  })

  it('tries curated candidates in order and picks the first one actually present', () => {
    const zira = makeVoice({ name: 'Microsoft Zira Desktop', lang: 'en-US' })
    const result = selectPersonaVoice('en', [zira], 'chromium')

    expect(result).toEqual({ voice: zira, source: 'curated' })
  })

  it('falls back to the scored heuristic when no curated name is present for a curated language', () => {
    const male = makeVoice({ name: 'Google UK English Male', lang: 'en-GB' })
    const femaleNamed = makeVoice({ name: 'Some Random Female Voice', lang: 'en-GB' })
    const result = selectPersonaVoice('en', [male, femaleNamed], 'chromium')

    expect(result).toEqual({ voice: femaleNamed, source: 'heuristic' })
  })

  it('falls back to the heuristic entirely for an uncurated (language, engine) pair', () => {
    const arabicVoice = makeVoice({ name: 'Microsoft Naayf', lang: 'ar-SA' })
    const result = selectPersonaVoice('ar', [arabicVoice], 'webkit')

    expect(result).toEqual({ voice: arabicVoice, source: 'heuristic' })
  })

  it('falls back to the heuristic for an unknown browser engine', () => {
    const voice = makeVoice({ name: 'Samantha', lang: 'en-US' })
    const result = selectPersonaVoice('en', [voice], 'unknown')

    expect(result).toEqual({ voice, source: 'heuristic' })
  })

  it('prefers a local service voice as a heuristic tiebreaker when name scores tie', () => {
    const remote = makeVoice({ name: 'Karen Remote', lang: 'en-AU', localService: false })
    const local = makeVoice({ name: 'Karen Local', lang: 'en-AU', localService: true })
    const result = selectPersonaVoice('en', [remote, local], 'unknown')

    expect(result).toEqual({ voice: local, source: 'heuristic' })
  })

  it('never returns an unrelated-language voice — returns null with source "none" when nothing matches the language', () => {
    const frenchVoice = makeVoice({ name: 'Amélie', lang: 'fr-CA' })
    const result = selectPersonaVoice('de', [frenchVoice], 'chromium')

    expect(result).toEqual({ voice: null, source: 'none' })
  })

  it('returns null with source "none" when the voice list is empty', () => {
    const result = selectPersonaVoice('en', [], 'chromium')

    expect(result).toEqual({ voice: null, source: 'none' })
  })

  it('matches language by prefix (e.g. "en" matches "en-US")', () => {
    const voice = makeVoice({ name: 'Zira', lang: 'en-US' })
    const result = selectPersonaVoice('en', [voice], 'unknown')

    expect(result.voice).toBe(voice)
  })
})
