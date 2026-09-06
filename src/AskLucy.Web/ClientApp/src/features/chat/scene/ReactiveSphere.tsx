import { useFrame } from '@react-three/fiber'
import { type RefObject, useMemo, useRef } from 'react'
import { createNoise2D } from 'simplex-noise'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
import { getDotMeshColors } from './dotMeshTheme'
import { computeBreathValue } from './sphereBreath'
import fragmentShader from './sphere.frag.glsl?raw'
import vertexShader from './sphere.vert.glsl?raw'

const IDLE_AMPLITUDE = 0.06
const IDLE_FREQUENCY = 1.4
const REACTIVE_AMPLITUDE_MAX = 0.35
const REACTIVE_FREQUENCY_MAX = 2.2
const IDLE_ROTATION_SPEED = 0.08 // rad/s
const SPHERE_RADIUS = 1.4
const BREATH_FREQUENCY = 0.6 // rad/s — slower than IDLE_FREQUENCY's noise wobble
const BREATH_AMPLITUDE = 0.035 // subtle relative to REACTIVE_AMPLITUDE_MAX's 0.35

// Icosahedron subdivision level per quality tier (live user review, 2026-09-06 — replaces the
// particle-cloud's POINT_COUNT_BY_TIER now that the sphere is a continuous mesh, not points).
// 'full' is smooth enough at typical card sizes without pushing triangle count needlessly high
// (detail 5 → 20,480 triangles, trivial for any WebGL2-capable GPU at this scene's scale);
// 'reduced' stays coarse for the mobile breakpoint (research.md §5's simpler-technique intent).
const ICOSAHEDRON_DETAIL_BY_TIER = { full: 5, reduced: 2 } as const

// A flat-shaded opaque surface has no additive-overlap saturation risk (unlike the former
// particle cloud — see sphere.frag.glsl's history), so this no longer needs a per-tier value.
const SPHERE_INTENSITY = 1

interface ReactiveSphereProps {
  /** Ref-based getter for the 0 (silent) – 1 (loud) damped TTS envelope (useTextToSpeech's
   * `getIntensity`, FR-018, research.md §3) — read every frame here rather than passed as
   * a plain number prop, so the assistant's voice doesn't force a React re-render per frame. */
  getReactiveIntensity: () => number
  /** research.md §4/§5 — 'full' uses a finer icosahedron subdivision (ICOSAHEDRON_DETAIL_BY_TIER);
   * 'reduced' uses a coarser one; 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** FR-011: freezes idle rotation/breathing and caps reactive amplitude when the user prefers reduced motion. */
  reducedMotion: boolean
  /** Optional external ref to the sphere's outer `<group>` — SceneBackground.tsx passes this
   * through to `ParticleSphereBloom`'s `selection` so only this object blooms (FR-004,
   * research.md §3). Falls back to an internal ref when omitted so this component still works
   * standalone (e.g. in isolation, without a bloom pass mounted). */
  groupRef?: RefObject<THREE.Group | null>
}

/** The workspace's abstract, audio-reactive organic sphere (spec.md Clarifications — not
 * a geographic globe). A continuous noise-displaced mesh, not a particle cloud (pivoted
 * 2026-09-06, live user review — see sphere.vert.glsl's history for why). Idles via a slow
 * noise-driven wobble/rotation plus a breathing pulse (FR-006, sphereBreath.ts); deforms
 * further while the assistant is speaking (getReactiveIntensity() above zero); surface color
 * follows the current theme (FR-008, dotMeshTheme.ts) with fresnel rim-lighting (sphere.frag.glsl). */
export function ReactiveSphere({
  getReactiveIntensity,
  qualityTier,
  reducedMotion,
  groupRef: externalGroupRef,
}: ReactiveSphereProps) {
  const materialRef = useRef<THREE.ShaderMaterial>(null)
  const internalGroupRef = useRef<THREE.Group>(null)
  const groupRef = externalGroupRef ?? internalGroupRef
  const elapsed = useRef(0)
  const mode = useThemeStore((s) => s.mode)
  const wobbleNoise = useMemo(() => createNoise2D(), [])

  const dotColors = getDotMeshColors(mode)

  const uniforms = useMemo(
    () => ({
      uTime: { value: 0 },
      uAmplitude: { value: IDLE_AMPLITUDE },
      uFrequency: { value: IDLE_FREQUENCY },
      uBreath: { value: 0 },
      uIntensity: { value: SPHERE_INTENSITY },
      uColorIdle: { value: new THREE.Color(dotColors.idle) },
      uColorReactive: { value: new THREE.Color(dotColors.reactive) },
    }),
    // Initial values only — mode changes are applied to the existing uniforms in useFrame
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
        const reactiveIntensity = getReactiveIntensity()
        u.uAmplitude.value = IDLE_AMPLITUDE + reactiveIntensity * REACTIVE_AMPLITUDE_MAX
        u.uFrequency.value =
          IDLE_FREQUENCY + reactiveIntensity * (REACTIVE_FREQUENCY_MAX - IDLE_FREQUENCY)
        // FR-006: a slow idle breathing pulse, additive with the noise/reactive displacement
        // above (sphere.vert.glsl) so it layers with voice reactivity rather than competing
        // with it (Acceptance Scenario 3).
        u.uBreath.value = computeBreathValue(elapsed.current, BREATH_FREQUENCY, BREATH_AMPLITUDE)
      } else {
        // FR-011: reduced motion freezes continuous rotation, breathing, and reactive growth —
        // the noise pattern holds still rather than continuing to animate.
        u.uAmplitude.value = IDLE_AMPLITUDE
        u.uFrequency.value = IDLE_FREQUENCY
        u.uBreath.value = 0
      }

      // FR-008/SC-004: write the current theme's colors into the uniforms in place every
      // frame (cheap — two Color.set calls) rather than recreating the material on theme
      // toggle, so the switch applies with no perceptible delay/flash.
      u.uColorIdle.value.set(dotColors.idle)
      u.uColorReactive.value.set(dotColors.reactive)
    }

    if (groupRef.current && !reducedMotion) {
      const wobble = wobbleNoise(elapsed.current * 0.05, 0)
      groupRef.current.rotation.y += (IDLE_ROTATION_SPEED + wobble * 0.02) * delta
    }
  })

  return (
    <group ref={groupRef}>
      <mesh>
        <icosahedronGeometry args={[SPHERE_RADIUS, ICOSAHEDRON_DETAIL_BY_TIER[qualityTier]]} />
        <shaderMaterial
          ref={materialRef}
          vertexShader={vertexShader}
          fragmentShader={fragmentShader}
          uniforms={uniforms}
        />
      </mesh>
    </group>
  )
}
