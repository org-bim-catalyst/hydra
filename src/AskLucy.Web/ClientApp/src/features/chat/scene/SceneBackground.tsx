import { OrbitControls, PerformanceMonitor } from '@react-three/drei'
import { Canvas } from '@react-three/fiber'
import { Box } from '@mui/material'
import { Component, type ReactNode, useRef, useState } from 'react'
import type { Group } from 'three'
import { ParticleSphereBloom } from './ParticleSphereBloom'
import { ReactiveSphere } from './ReactiveSphere'
import { getSphereRenderTechnique } from './sphereRenderTechnique'
import { useSceneQualityTier } from './useSceneQualityTier'

interface SceneBackgroundProps {
  /** Forwarded to the sphere unchanged (FR-018) — see ReactiveSphere's own doc comment. */
  getReactiveIntensity: () => number
}

class SceneErrorBoundary extends Component<
  { children: ReactNode; fallback: ReactNode },
  { hasError: boolean }
> {
  state = { hasError: false }

  static getDerivedStateFromError() {
    return { hasError: true }
  }

  componentDidCatch(error: unknown) {
    // constitution §2.VIII: catching without surfacing is a violation — a decorative
    // background failing shouldn't show the user a toast, but it must not vanish
    // silently from telemetry either.
    console.error('3D scene failed to render; falling back to the static background.', error)
  }

  render() {
    return this.state.hasError ? this.props.fallback : this.props.children
  }
}

/** FR-011: non-3D fallback shown when WebGL2 is unavailable, or the scene render fails. */
function StaticFallback() {
  return (
    <Box
      aria-hidden="true"
      sx={{
        position: 'absolute',
        inset: 0,
        zIndex: 0,
        background: (theme) =>
          theme.palette.mode === 'dark'
            ? 'radial-gradient(circle at 50% 40%, #1D1B17 0%, #14130F 70%)'
            : 'radial-gradient(circle at 50% 40%, #FFFFFF 0%, #F7F6F2 70%)',
      }}
    />
  )
}

/** FR-001/FR-003: the full-viewport 3D scene layer behind the assistant panel. Renders
 * the static fallback instead of mounting a `<Canvas>` at all when WebGL2 is unavailable
 * (useSceneQualityTier), and falls back the same way if the scene throws while rendering. */
export function SceneBackground({ getReactiveIntensity }: SceneBackgroundProps) {
  const { tier, prefersReducedMotion, reportPerformanceRegression } = useSceneQualityTier()
  // FR-021/SC-011: the placeholder is already visible synchronously (it's what the
  // Suspense boundary in ChatPage.tsx shows while this chunk loads); this local
  // `isReady` flag just cross-fades the canvas in on top of it once R3F's `onCreated`
  // signals the WebGL context actually exists, instead of popping in abruptly.
  const [isReady, setIsReady] = useState(false)
  // FR-004/research.md §3: shared with ParticleSphereBloom's `selection` so the scoped bloom
  // pass targets exactly this object, not the rest of the scene.
  const sphereGroupRef = useRef<Group>(null)

  if (tier === 'static-fallback') {
    return <StaticFallback />
  }

  // FR-004/FR-010: bloom is part of the "full" tier's richer technique only — the "reduced"
  // tier's simpler technique (sphereRenderTechnique.ts) never mounts the bloom pass.
  const { bloomEnabled } = getSphereRenderTechnique(tier)
  // Temporarily disabled at the call site (live user review): SelectiveBloom's
  // `luminanceSmoothing` filter made the sphere visibly "ramp up" from crisp individual
  // dots into a blurred glow over the first couple of seconds after mount — a real bug in
  // the effect's adaptive convergence, not just an intensity/threshold tuning problem — and
  // the reference image/implementation this feature is meant to match has no glow at all.
  // The plumbing (ParticleSphereBloom, bloomEnabled) stays in place and unit-tested for
  // later opt-in polish; it's just not wired into the live scene right now.
  const BLOOM_TEMPORARILY_DISABLED = true

  return (
    <SceneErrorBoundary fallback={<StaticFallback />}>
      <StaticFallback />
      <Box
        aria-hidden="true"
        tabIndex={-1}
        sx={{
          position: 'absolute',
          inset: 0,
          zIndex: 0,
          opacity: isReady ? 1 : 0,
          transition: (t) => t.transitions.create('opacity', { duration: t.transitions.duration.complex }),
          '& canvas': { outline: 'none' },
        }}
      >
        <Canvas
          dpr={[1, 2]}
          camera={{ position: [0, 0, 8], fov: 45 }}
          onCreated={() => setIsReady(true)}
        >
          {/* research.md §4: a one-way ratchet from 'full' to 'reduced' on sustained
              frame-time regression — no re-upgrade, no continuous LOD (KISS/YAGNI).
              Disabled while BLOOM_TEMPORARILY_DISABLED is true: the only visible
              difference between tiers is additive vs normal blending, which only
              matters when bloom is active. Monitoring without bloom causes the sphere
              to permanently downgrade its blending for no user benefit. */}
          {!BLOOM_TEMPORARILY_DISABLED && (
            <PerformanceMonitor onDecline={reportPerformanceRegression} />
          )}
          <ambientLight intensity={0.6} />
          <ReactiveSphere
            getReactiveIntensity={getReactiveIntensity}
            qualityTier={tier}
            reducedMotion={prefersReducedMotion}
            groupRef={sphereGroupRef}
          />
          {bloomEnabled && !BLOOM_TEMPORARILY_DISABLED && (
            <ParticleSphereBloom sphereRef={sphereGroupRef} />
          )}
          <OrbitControls
            enablePan
            enableZoom
            enableRotate
            enableDamping
            dampingFactor={0.08}
            minDistance={5}
            maxDistance={16}
          />
        </Canvas>
      </Box>
    </SceneErrorBoundary>
  )
}
