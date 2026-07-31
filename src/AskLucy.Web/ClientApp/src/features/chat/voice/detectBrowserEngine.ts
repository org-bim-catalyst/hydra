import type { BrowserEngine } from './voicePersonaMap'

interface NavigatorUAData {
  brands?: Array<{ brand: string }>
}

/** Pure, side-effect-free browser-engine sniffing (research.md §3) — a session-stable
 * classification, not a full UA parser. Returns 'unknown' rather than throwing when
 * neither `userAgentData` nor `userAgent` yields a confident match; callers treat
 * 'unknown' the same as "no curated entry" and fall straight to the heuristic tier. */
export function detectBrowserEngine(nav: Navigator = navigator): BrowserEngine | 'unknown' {
  const uaData = (nav as Navigator & { userAgentData?: NavigatorUAData }).userAgentData
  const brands = uaData?.brands?.map((b) => b.brand.toLowerCase()) ?? []

  if (brands.some((b) => b.includes('chromium') || b.includes('chrome') || b.includes('edge'))) {
    return 'chromium'
  }

  const ua = nav.userAgent?.toLowerCase() ?? ''
  if (!ua) return 'unknown'

  if (ua.includes('firefox')) return 'firefox'
  if (ua.includes('edg/') || ua.includes('chrome/') || ua.includes('chromium')) return 'chromium'
  if (ua.includes('safari') && !ua.includes('chrome') && !ua.includes('chromium')) return 'webkit'

  return 'unknown'
}
