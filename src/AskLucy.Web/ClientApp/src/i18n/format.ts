import { DEFAULT_LANGUAGE, type Language } from './types'

/**
 * Locale tags for `Intl`. Arabic always uses Western digits (`nu-latn`) and the Gregorian calendar
 * (`ca-gregory`) with Arabic month names (Assumptions). English passes `undefined`, i.e. the browser's own
 * locale, exactly as the screens did before localization, so English rendering does not change.
 */
const NUMBER_LOCALE: Record<Language, string | undefined> = { en: undefined, ar: 'ar-u-nu-latn' }
const DATE_LOCALE: Record<Language, string | undefined> = {
  en: undefined,
  ar: 'ar-u-ca-gregory-nu-latn',
}

export function formatNumber(
  value: number,
  language: Language = DEFAULT_LANGUAGE,
  options?: Intl.NumberFormatOptions,
): string {
  return new Intl.NumberFormat(NUMBER_LOCALE[language], options).format(value)
}

export function formatDate(
  value: Date | string | number,
  language: Language = DEFAULT_LANGUAGE,
  options: Intl.DateTimeFormatOptions = { dateStyle: 'medium' },
): string {
  return new Intl.DateTimeFormat(DATE_LOCALE[language], options).format(new Date(value))
}

const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 60 * 60],
  ['month', 30 * 24 * 60 * 60],
  ['week', 7 * 24 * 60 * 60],
  ['day', 24 * 60 * 60],
  ['hour', 60 * 60],
  ['minute', 60],
]

export interface RelativeOptions {
  now?: Date
  /** What to show under a minute (a translated "just now"); without it the formatter's own "now" is used. */
  justNow?: string
}

/** Zero-dependency relative time through `Intl.RelativeTimeFormat` — "3 hours ago", "منذ 3 ساعات". */
export function formatRelative(
  value: Date | string | number,
  language: Language = DEFAULT_LANGUAGE,
  { now = new Date(), justNow }: RelativeOptions = {},
): string {
  const formatter = new Intl.RelativeTimeFormat(DATE_LOCALE[language], { numeric: 'auto' })
  const seconds = Math.round((new Date(value).getTime() - now.getTime()) / 1000)
  if (Math.abs(seconds) < 60) return justNow ?? formatter.format(0, 'second')

  for (const [unit, secondsInUnit] of UNITS) {
    if (Math.abs(seconds) >= secondsInUnit) {
      return formatter.format(Math.round(seconds / secondsInUnit), unit)
    }
  }
  return formatter.format(Math.round(seconds / 60), 'minute')
}
