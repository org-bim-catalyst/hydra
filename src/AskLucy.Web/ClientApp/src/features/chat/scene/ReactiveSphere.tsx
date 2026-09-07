import { useFrame } from '@react-three/fiber'
import { type RefObject, useMemo, useRef } from 'react'
import { createNoise2D } from 'simplex-noise'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'
import { getDotMeshColors } from './dotMeshTheme'
import { generateFibonacciSpherePositions } from './generateFibonacciSpherePositions'
import { computeBreathValue } from './sphereBreath'
import { getSphereRenderTechnique } from './sphereRenderTechnique'
import fragmentShader from './sphere.frag.glsl?raw'
import vertexShader from './sphere.vert.glsl?raw'

const IDLE_AMPLITUDE = 0.06
const IDLE_FREQUENCY = 1.4
const REACTIVE_AMPLITUDE_MAX = 0.35
const REACTIVE_FREQUENCY_MAX = 2.2
const IDLE_ROTATION_SPEED = 0.08 // rad/s
const SPHERE_RADIUS = 1.4
const BREATH_FREQUENCY = 0.6 // rad/s - slower than IDLE_FREQUENCY's noise wobble
const BREATH_AMPLITUDE = 0.035 // subtle relative to REACTIVE_AMPLITUDE_MAX's 0.35

// Restored 2026-09-07 to the spec 011-particle-sphere-engine design (a uniform
// Fibonacci-distributed particle sphere), after a full day exploring an alternative
// continuous-mesh technique - explicit live user preference: "I liked the specs version
// more." Point count/size tuning below is the state this project's particle sphere had already
// reached through real production incidents before that detour (an RTX 3080 NaN bug, two
// separate additive-blending saturation incidents) - kept as-is, since those fixes remain
// correct regardless of which technique the app ultimately uses.
const POINT_COUNT_BY_TIER = { full: 8000, reduced: 500 } as const
const BASE_POINT_SIZE_BY_TIER = { full: 0.12, reduced: 0.3 } as const
// 'full' lowered further from that restored 0.55 (live user report, 2026-09-07, RTX 3080:
// "points not clear... too much glow and brightness" - the same additive-oversaturation
// symptom those prior incidents describe). 0.55 was tuned back when SphereBloom.tsx's bloom
// pass was permanently disabled; now that it genuinely renders on top of this same additive
// particle brightness, the combined result reads as oversaturated again on some GPUs even
// though neither piece alone regressed.
const INTENSITY_BY_TIER = { full: 0.4, reduced: 1 } as const

interface ReactiveSphereProps {
  /** Ref-based getter for real low/mid/high frequency bands (useVoiceAnalyzer's
   * `getFrequencyBands`) - read every frame here rather than passed as plain number props, so
   * the assistant's voice doesn't force a React re-render per frame. Replaces this component's
   * previous single damped-TTS-envelope approximation (`getReactiveIntensity`) with the same
   * real Web Audio `AnalyserNode` data the rest of the scene already uses - a data-quality
   * upgrade with no change to the reactive *behavior* below (still one combined intensity
   * value driving amplitude/frequency, matching spec 011's original design), just a better
   * source for it. */
  getFrequencyBands: () => FrequencyBands
  /** 'full' uses a denser dot lattice and additive-blended glow rendering technique; 'reduced'
   * uses a lower count and simpler normal-blended technique (sphereRenderTechnique.ts);
   * 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** Freezes idle rotation/breathing and caps reactive amplitude when the user prefers reduced motion. */
  reducedMotion: boolean
  /** Optional external ref to the sphere's outer `<group>` - SceneBackground.tsx passes this
   * through to `SphereBloom`'s `selection` so only this object blooms. Falls back to an
   * internal ref when omitted so this component still works standalone (e.g. in isolation,
   * without a bloom pass mounted). */
  groupRef?: RefObject<THREE.Group | null>
}

/** The workspace's abstract, audio-reactive dot-mesh sphere (spec.md Clarifications - not a
 * geographic globe; spec 011-particle-sphere-engine FR-001 - a uniform Fibonacci-distributed
 * mesh of dots, not concentric rings or a solid shaded surface). Idles via a slow noise-driven
 * wobble/rotation plus a breathing pulse (FR-006, sphereBreath.ts); deforms further while the
 * assistant is speaking; dot colors follow the current theme (FR-008, dotMeshTheme.ts). */
export function ReactiveSphere({
  getFrequencyBands,
  qualityTier,
  reducedMotion,
  groupRef: externalGroupRef,
}: ReactiveSphereProps) {
  const materialRef = useRef<THREE.ShaderMaterial>(null)
  const internalGroupRef = useRef<THREE.Group>(null)
  const groupRef = externalGroupRef ?? internalGroupRef
  const wobbleNoise = useMemo(() => createNoise2D(), [])
  const elapsed = useRef(0)
  const mode = useThemeStore((s) => s.mode)

  const positions = useMemo(
    () => generateFibonacciSpherePositions(POINT_COUNT_BY_TIER[qualityTier], SPHERE_RADIUS),
    [qualityTier],
  )

  const { blending } = getSphereRenderTechnique(qualityTier)

  const dotColors = getDotMeshColors(mode)

  const uniforms = useMemo(
    () => ({
      uTime: { value: 0 },
      uAmplitude: { value: IDLE_AMPLITUDE },
      uFrequency: { value: IDLE_FREQUENCY },
      uBreath: { value: 0 },
      uBasePointSize: { value: BASE_POINT_SIZE_BY_TIER[qualityTier] },
      uIntensity: { value: INTENSITY_BY_TIER[qualityTier] },
      uColorIdle: { value: new THREE.Color(dotColors.idle) },
      uColorReactive: { value: new THREE.Color(dotColors.reactive) },
    }),
    // Initial values only - mode changes are applied to the existing uniforms in useFrame
    // below instead of recreating the material on every theme toggle.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [],
  )

  useFrame((_, delta) => {
    const material = materialRef.current
    if (material) {
      const u = material.uniforms as typeof uniforms
      if (!reducedMotion) {
        elapsed.current += delta
        u.uTime.value = elapsed.current
        const bands = getFrequencyBands()
        const reactiveIntensity = Math.max(bands.low, bands.mid, bands.high)
        u.uAmplitude.value = IDLE_AMPLITUDE + reactiveIntensity * REACTIVE_AMPLITUDE_MAX
        u.uFrequency.value =
          IDLE_FREQUENCY + reactiveIntensity * (REACTIVE_FREQUENCY_MAX - IDLE_FREQUENCY)
        // A slow idle breathing pulse, additive with the noise/reactive displacement above
        // (sphere.vert.glsl) so it layers with voice reactivity rather than competing with it.
        u.uBreath.value = computeBreathValue(elapsed.current, BREATH_FREQUENCY, BREATH_AMPLITUDE)
      } else {
        // Reduced motion freezes continuous rotation, breathing, and reactive growth - the
        // noise pattern holds still rather than continuing to animate.
        u.uAmplitude.value = IDLE_AMPLITUDE
        u.uFrequency.value = IDLE_FREQUENCY
        u.uBreath.value = 0
      }

      // Write the current theme's colors into the uniforms in place every frame (cheap - two
      // Color.set calls) rather than recreating the material on theme toggle, so the switch
      // applies with no perceptible delay/flash.
      u.uColorIdle.value.set(dotColors.idle)
      u.uColorReactive.value.set(dotColors.reactive)
      // Same rationale as above, for the tier-derived point size/intensity - a
      // performance-regression downgrade to 'reduced' must pick up its own, non-saturating
      // values immediately, not keep 'full's settings from before the downgrade.
      u.uBasePointSize.value = BASE_POINT_SIZE_BY_TIER[qualityTier]
      u.uIntensity.value = INTENSITY_BY_TIER[qualityTier]
    }

    if (groupRef.current && !reducedMotion) {
      const wobble = wobbleNoise(elapsed.current * 0.05, 0)
      groupRef.current.rotation.y += (IDLE_ROTATION_SPEED + wobble * 0.02) * delta
    }
  })

  return (
    <group ref={groupRef}>
      <points>
        <bufferGeometry>
          <bufferAttribute attach="attributes-position" args={[positions, 3]} />
        </bufferGeometry>
        <shaderMaterial
          ref={materialRef}
          vertexShader={vertexShader}
          fragmentShader={fragmentShader}
          uniforms={uniforms}
          transparent
          depthWrite={false}
          blending={blending}
        />
      </points>
    </group>
  )
}
