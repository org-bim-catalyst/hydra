import { OrbitControls, PerformanceMonitor } from '@react-three/drei'
import { Canvas } from '@react-three/fiber'
import { Box } from '@mui/material'
import { Component, type ReactNode, useRef, useState } from 'react'
import type { Group } from 'three'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'
import { ReactiveSphere } from './ReactiveSphere'
import { SphereBloom } from './SphereBloom'
import { useSceneQualityTier } from './useSceneQualityTier'

interface SceneBackgroundProps {
  /** Forwarded to the sphere unchanged (FR-018) — see ReactiveSphere's own doc comment. */
  getFrequencyBands: () => FrequencyBands
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

/**
 * FR-011: non-3D fallback shown when WebGL2 is unavailable, or the scene render fails.
 *
 * Also used as the backdrop while the canvas is still coming up, which is why it takes a
 * `visible` flag. It is opaque by design — as a *fallback* that is correct, but left painted
 * underneath a live canvas it defeats the canvas's own transparency entirely, which is exactly
 * what kept this card solid regardless of the renderer's alpha settings.
 */
function StaticFallback({ visible = true }: { visible?: boolean }) {
  return (
    <Box
      aria-hidden="true"
      sx={{
        position: 'absolute',
        inset: 0,
        zIndex: 0,
        opacity: visible ? 1 : 0,
        transition: (t) => t.transitions.create('opacity', { duration: t.transitions.duration.complex }),
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
export function SceneBackground({ getFrequencyBands }: SceneBackgroundProps) {
  const { tier, prefersReducedMotion, reportPerformanceRegression } = useSceneQualityTier()
  // FR-021/SC-011: the placeholder is already visible synchronously (it's what the
  // Suspense boundary in ChatPage.tsx shows while this chunk loads); this local
  // `isReady` flag just cross-fades the canvas in on top of it once R3F's `onCreated`
  // signals the WebGL context actually exists, instead of popping in abruptly.
  const [isReady, setIsReady] = useState(false)
  // FR-004/research.md §3: shared with SphereBloom's `selection` so the scoped bloom pass
  // targets exactly this object, not the rest of the scene.
  const sphereGroupRef = useRef<Group>(null)

  if (tier === 'static-fallback') {
    return <StaticFallback />
  }

  return (
    <SceneErrorBoundary fallback={<StaticFallback />}>
      {/* Fades out as the canvas fades in, so what sits behind this card shows through the
          transparent scene rather than through nothing. */}
      <StaticFallback visible={!isReady} />
      <Box
        aria-hidden="true"
        tabIndex={-1}
        sx={{
          position: 'absolute',
          inset: 0,
          zIndex: 0,
          opacity: isReady ? 1 : 0,
          transition: (t) => t.transitions.create('opacity', { duration: t.transitions.duration.complex }),
          // Belt and braces: a background on the canvas or its wrapper would hide the
          // renderer's alpha just as effectively as clearing opaque.
          '& canvas': { outline: 'none', background: 'transparent' },
          // Live user review, 2026-09-07: bloom alone (SphereBloom.tsx) only separates the
          // sphere from a *dark* card — brightening pixels against a light-mode card just
          // blends into it. CSS `filter: drop-shadow()` (unlike `box-shadow`, which is
          // rectangular) reads the canvas's own alpha channel — opaque where the sphere is
          // drawn, fully transparent everywhere else (the `gl.setClearAlpha(0)` above) — so
          // the shadow hugs the sphere's actual silhouette, the same way a Photoshop layer
          // style's Drop Shadow/Outer Glow follows a layer's alpha rather than its bounding
          // box. Darker/stronger in light mode, where separation is otherwise hardest.
          filter: (t) =>
            t.palette.mode === 'dark'
              ? 'drop-shadow(0 4px 14px rgba(0, 0, 0, 0.45))'
              : 'drop-shadow(0 4px 16px rgba(0, 0, 0, 0.4))',
        }}
      >
        <Canvas
          dpr={[1, 2]}
          camera={{ position: [0, 0, 8], fov: 45 }}
          // `alpha: true` is what lets the canvas composite over the page at all; without it
          // WebGL clears to an opaque buffer no matter what clear colour is set.
          gl={{ alpha: true }}
          onCreated={({ gl, scene }) => {
            // Fully transparent clear, so the card's own translucent background — and the map
            // behind it — is what shows between the dots.
            gl.setClearColor(0x000000, 0)
            gl.setClearAlpha(0)
            // A scene background would paint over the cleared buffer and undo the above.
            scene.background = null
            setIsReady(true)
          }}
        >
          {/* research.md §4: a one-way ratchet from 'full' to 'reduced' on sustained
              frame-time regression — no re-upgrade, no continuous LOD (KISS/YAGNI). 'reduced'
              now means a coarser SphereGeometry subdivision (ReactiveSphere.tsx's
              SEGMENTS_BY_TIER) rather than a different blending mode. */}
          <PerformanceMonitor onDecline={reportPerformanceRegression} />
          <ambientLight intensity={0.6} />
          <ReactiveSphere
            getFrequencyBands={getFrequencyBands}
            qualityTier={tier}
            reducedMotion={prefersReducedMotion}
            groupRef={sphereGroupRef}
          />
          <SphereBloom sphereRef={sphereGroupRef} />
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
