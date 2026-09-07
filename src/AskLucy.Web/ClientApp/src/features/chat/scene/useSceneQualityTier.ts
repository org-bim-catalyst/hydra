import { useCallback, useEffect, useRef, useState } from 'react'
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

/** FR-011/FR-012/FR-020, research.md §4: decides which of three discrete quality tiers
 * the 3D scene should render at. `static-fallback` is permanent for the session (no
 * WebGL2, or the scene's error boundary caught a render failure — see SceneBackground).
 * `reportPerformanceRegression` is a one-way ratchet from `full` down to `reduced`,
 * called by the scene's PerformanceMonitor (T022) on sustained frame-time regression —
 * kept intentionally simple (no re-upgrade, no continuous LOD) per constitution
 * §2.III KISS/YAGNI. */
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

  // Timestamp captured once the hook is mounted. Null until the first effect fires;
  // declines that arrive before it (or within the guard window) are silently dropped —
  // the one-time GPU spike from Google Maps WebGL Overlay initialization is
  // indistinguishable from a real device-level regression by frame-time alone. Widened
  // from 10s to 20s (live user report, 2026-09-07, on an RTX 4060): once the sphere's
  // bloom pass (SphereBloom.tsx) actually started rendering for the first time, its own
  // shader-compile/render-target warm-up cost combined with the map's own init spike to
  // still exceed the original 10s window, so a decline reported right as the guard lifted
  // read as "real" and permanently demoted the sphere even though both spikes were
  // transient startup cost, not a sustained regression.
  const mountedAt = useRef<number | null>(null)
  useEffect(() => {
    mountedAt.current = performance.now()
  }, [])

  // Temporary diagnostic instrumentation (live user report, 2026-09-07: three targeted fixes
  // in a row produced "no difference," so the next step is real data instead of another guess).
  // Remove once the actual mechanism behind the sphere's post-mount quality drop is confirmed.
  useEffect(() => {
    console.info(`[SceneQualityTier] tier=${tier} at t=${performance.now().toFixed(0)}ms`)
  }, [tier])

  const reportPerformanceRegression = useCallback(() => {
    const elapsed = mountedAt.current === null ? null : performance.now() - mountedAt.current
    const withinGuard = elapsed === null || elapsed < 20_000
    console.info(
      `[SceneQualityTier] reportPerformanceRegression called at elapsed=${elapsed?.toFixed(0)}ms ` +
        `(guard=20000ms) -> ${withinGuard ? 'IGNORED (within guard)' : 'ACCEPTED (demoting to reduced)'}`,
    )
    if (withinGuard) return
    setTier((current) => (current === 'full' ? 'reduced' : current))
  }, [])

  return { tier, prefersReducedMotion, reportPerformanceRegression }
}
