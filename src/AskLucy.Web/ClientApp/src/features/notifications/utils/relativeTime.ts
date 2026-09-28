const UNITS: [Intl.RelativeTimeFormatUnit, number][] = [
  ['year', 365 * 24 * 60 * 60],
  ['month', 30 * 24 * 60 * 60],
  ['week', 7 * 24 * 60 * 60],
  ['day', 24 * 60 * 60],
  ['hour', 60 * 60],
  ['minute', 60],
]

const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })

/** Zero-dependency relative time (no date library in this project) — "3 hours ago", "just now". */
export function formatRelativeTime(isoDateUtc: string, now: Date = new Date()): string {
  const seconds = Math.round((new Date(isoDateUtc).getTime() - now.getTime()) / 1000)
  if (Math.abs(seconds) < 60) return 'just now'

  for (const [unit, secondsInUnit] of UNITS) {
    if (Math.abs(seconds) >= secondsInUnit) {
      return formatter.format(Math.round(seconds / secondsInUnit), unit)
    }
  }
  return formatter.format(Math.round(seconds / 60), 'minute')
}
