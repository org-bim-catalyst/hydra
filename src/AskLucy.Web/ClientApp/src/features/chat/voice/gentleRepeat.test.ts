import { describe, expect, it } from 'vitest'
import { GENTLE_REPEAT_BY_LANGUAGE, gentleRepeatMessage } from './gentleRepeat'

describe('gentleRepeatMessage', () => {
  it('returns the English phrase by default', () => {
    expect(gentleRepeatMessage()).toBe(GENTLE_REPEAT_BY_LANGUAGE.en)
  })

  it('returns the phrase for a supported language', () => {
    expect(gentleRepeatMessage('ar')).toBe(GENTLE_REPEAT_BY_LANGUAGE.ar)
    expect(gentleRepeatMessage('es')).toBe(GENTLE_REPEAT_BY_LANGUAGE.es)
    expect(gentleRepeatMessage('fr')).toBe(GENTLE_REPEAT_BY_LANGUAGE.fr)
    expect(gentleRepeatMessage('de')).toBe(GENTLE_REPEAT_BY_LANGUAGE.de)
  })

  it('matches on the base subtag', () => {
    expect(gentleRepeatMessage('en-GB')).toBe(GENTLE_REPEAT_BY_LANGUAGE.en)
    expect(gentleRepeatMessage('ar-AE')).toBe(GENTLE_REPEAT_BY_LANGUAGE.ar)
  })

  it('falls back to English for an unsupported language', () => {
    expect(gentleRepeatMessage('ja')).toBe(GENTLE_REPEAT_BY_LANGUAGE.en)
  })
})
