import { useEffect, useState } from 'react'
import { usePrefersReducedMotion } from '../../../hooks/usePrefersReducedMotion'

export type SceneQualityTier = 'full' | 'reduced' | 'static-fallback'

/** Matches the MUI default 'sm' breakpoint (theme/tokens has no override) — kept as a
 * plain number here rather than reading the theme, so this hook stays WebGL/MUI-free
 * and unit-testable in isolation (plan.md Technical Context). */
const MOBILE_BREAKPOINT_PX = 600

function supportsWebGL2(): boolean {
  if (typeof document === 'undefined') return false
  try {
    const canvas = document.createElement('canvas')
    return Boolean(canvas.getContext('webgl2'))
  } catch {
    return false
  }
}

function matchMediaSafe(query: string): MediaQueryList | null {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return null
  return window.matchMedia(query)
}

function initialTier(): SceneQualityTier {
  if (!supportsWebGL2()) return 'static-fallback'
  const mobile = matchMediaSafe(`(max-width: ${MOBILE_BREAKPOINT_PX - 0.05}px)`)
  return mobile?.matches ? 'reduced' : 'full'
}

/** FR-011/FR-012/FR-020, research.md §4: decides which of three discrete quality tiers the 3D
 * scene should render at. `static-fallback` is permanent for the session (no WebGL2, or the
 * scene's error boundary caught a render failure — see SceneBackground). `full`/`reduced`
 * otherwise track the viewport breakpoint only — deterministic, not a reaction to runtime frame
 * rate.
 *
 * Previously also had a one-way `full` -> `reduced` ratchet driven by drei's
 * `PerformanceMonitor` (`reportPerformanceRegression`, removed here). Live user report,
 * 2026-09-09: the sphere kept getting demoted "randomly" even after several rounds of real,
 * confirmed root-cause fixes earlier this week (map GPU contention, a bloom-effect leak, additive
 * alpha accumulation) — each fixed a genuine mechanism, but the underlying approach (auto-demote
 * on any sustained fps dip, permanently, for the rest of the session, with no re-upgrade) meant
 * any transient dip from an unrelated cause — another tab, a background process, a momentary
 * hiccup anywhere on the page — could still permanently downgrade the sphere for no recoverable
 * reason. Removed outright rather than chasing the next trigger. */
export function useSceneQualityTier() {
  const [tier, setTier] = useState<SceneQualityTier>(initialTier)
  // SPEC-017 FR-010/research.md #4: sourced from the app-wide shared hook (rather than a
  // second, independent matchMedia subscription) so this scene's reduced-motion behavior
  // can never drift from every other surface's.
  const prefersReducedMotion = usePrefersReducedMotion()

  useEffect(() => {
    const mobileQuery = matchMediaSafe(`(max-width: ${MOBILE_BREAKPOINT_PX - 0.05}px)`)
    if (!mobileQuery) return

    const onMobileChange = () => {
      // A no-WebGL2 fallback never upgrades; otherwise reflect the current breakpoint.
      setTier((current) =>
        current === 'static-fallback' ? current : mobileQuery.matches ? 'reduced' : 'full',
      )
    }

    mobileQuery.addEventListener('change', onMobileChange)
    return () => mobileQuery.removeEventListener('change', onMobileChange)
  }, [])

  return { tier, prefersReducedMotion }
}
