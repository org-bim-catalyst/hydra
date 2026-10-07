import { describe, expect, it } from 'vitest'
import { formatDate, formatNumber, formatRelative } from './format'

const ARABIC_INDIC_DIGITS = /[٠-٩۰-۹]/

describe('format (Assumptions: Western digits, Gregorian dates)', () => {
  it('formats Arabic numbers with Western digits (ar-u-nu-latn)', () => {
    const text = formatNumber(1234567.5, 'ar')
    expect(text).not.toMatch(ARABIC_INDIC_DIGITS)
    expect(text).toMatch(/1.234.567/)
  })

  it('formats Arabic dates in the Gregorian calendar with Arabic month names and Western digits', () => {
    const text = formatDate('2026-03-05T12:00:00Z', 'ar', {
      day: 'numeric',
      month: 'long',
      year: 'numeric',
      timeZone: 'UTC',
    })
    expect(text).not.toMatch(ARABIC_INDIC_DIGITS)
    expect(text).toContain('2026')
    expect(text).toContain('مارس')
    // The Hijri year for March 2026 is 1447; a Gregorian date must never show it.
    expect(text).not.toContain('1447')
  })

  it('formats relative time through Intl.RelativeTimeFormat, in Arabic with Western digits', () => {
    const now = new Date('2026-03-05T12:00:00Z')
    const text = formatRelative('2026-03-05T09:00:00Z', 'ar', { now })
    expect(text).toContain('3')
    expect(text).not.toMatch(ARABIC_INDIC_DIGITS)
    expect(text).toMatch(/[؀-ۿ]/)
  })

  it('formats relative time in English as before, with "just now" under a minute', () => {
    const now = new Date('2026-03-05T12:00:00Z')
    expect(formatRelative('2026-03-05T09:00:00Z', 'en', { now })).toBe('3 hours ago')
    expect(formatRelative('2026-03-04T12:00:00Z', 'en', { now })).toBe('yesterday')
    expect(formatRelative('2026-03-05T11:59:40Z', 'en', { now, justNow: 'just now' })).toBe(
      'just now',
    )
    expect(formatRelative('2026-03-05T11:59:40Z', 'ar', { now, justNow: 'الآن' })).toBe('الآن')
  })

  it('leaves English number formatting to the browser locale, as before', () => {
    expect(formatNumber(1234.5, 'en')).toBe((1234.5).toLocaleString(undefined))
  })
})
