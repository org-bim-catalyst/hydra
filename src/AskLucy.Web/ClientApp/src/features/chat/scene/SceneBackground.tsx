import { OrbitControls, PerformanceMonitor } from '@react-three/drei'
import { Canvas } from '@react-three/fiber'
import { Box } from '@mui/material'
import { Component, type ReactNode, useState } from 'react'
import { ReactiveSphere } from './ReactiveSphere'
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

  if (tier === 'static-fallback') {
    return <StaticFallback />
  }

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
          transition: 'opacity 400ms ease',
          '& canvas': { outline: 'none' },
        }}
      >
        <Canvas
          dpr={[1, 2]}
          camera={{ position: [0, 0, 4], fov: 45 }}
          onCreated={() => setIsReady(true)}
        >
          {/* research.md §4: a one-way ratchet from 'full' to 'reduced' on sustained
              frame-time regression — no re-upgrade, no continuous LOD (KISS/YAGNI). */}
          <PerformanceMonitor onDecline={reportPerformanceRegression} />
          <ambientLight intensity={0.6} />
          <ReactiveSphere
            getReactiveIntensity={getReactiveIntensity}
            qualityTier={tier}
            reducedMotion={prefersReducedMotion}
          />
          <OrbitControls
            enablePan
            enableZoom
            enableRotate
            enableDamping
            dampingFactor={0.08}
            minDistance={2.5}
            maxDistance={8}
          />
        </Canvas>
      </Box>
    </SceneErrorBoundary>
  )
}
