import { useCallback, useEffect, useState } from 'react'

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

/** FR-011/FR-012/FR-020, research.md §4: decides which of three discrete quality tiers
 * the 3D scene should render at. `static-fallback` is permanent for the session (no
 * WebGL2, or the scene's error boundary caught a render failure — see SceneBackground).
 * `reportPerformanceRegression` is a one-way ratchet from `full` down to `reduced`,
 * called by the scene's PerformanceMonitor (T022) on sustained frame-time regression —
 * kept intentionally simple (no re-upgrade, no continuous LOD) per constitution
 * §2.III KISS/YAGNI. */
export function useSceneQualityTier() {
  const [tier, setTier] = useState<SceneQualityTier>(initialTier)
  const [prefersReducedMotion, setPrefersReducedMotion] = useState(
    () => matchMediaSafe('(prefers-reduced-motion: reduce)')?.matches ?? false,
  )

  useEffect(() => {
    const reducedMotionQuery = matchMediaSafe('(prefers-reduced-motion: reduce)')
    const mobileQuery = matchMediaSafe(`(max-width: ${MOBILE_BREAKPOINT_PX - 0.05}px)`)
    if (!reducedMotionQuery && !mobileQuery) return

    const onReducedMotionChange = () =>
      setPrefersReducedMotion(Boolean(reducedMotionQuery?.matches))
    const onMobileChange = () => {
      // A no-WebGL2 fallback never upgrades; otherwise reflect the current breakpoint.
      setTier((current) =>
        current === 'static-fallback' ? current : mobileQuery?.matches ? 'reduced' : 'full',
      )
    }

    reducedMotionQuery?.addEventListener('change', onReducedMotionChange)
    mobileQuery?.addEventListener('change', onMobileChange)
    return () => {
      reducedMotionQuery?.removeEventListener('change', onReducedMotionChange)
      mobileQuery?.removeEventListener('change', onMobileChange)
    }
  }, [])

  const reportPerformanceRegression = useCallback(() => {
    setTier((current) => (current === 'full' ? 'reduced' : current))
  }, [])

  return { tier, prefersReducedMotion, reportPerformanceRegression }
}
