import { useFrame } from '@react-three/fiber'
import { type RefObject, useMemo, useRef } from 'react'
import { createNoise2D } from 'simplex-noise'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
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
const BREATH_FREQUENCY = 0.6 // rad/s — slower than IDLE_FREQUENCY's noise wobble
const BREATH_AMPLITUDE = 0.035 // subtle relative to REACTIVE_AMPLITUDE_MAX's 0.35

// spec 011-particle-sphere-engine FR-009/SC-002: point counts per quality tier — 'full' raised
// well beyond 010-lucy-brand-refresh's original 1400 for a visibly denser sphere. Manual
// testing (T008) found that pushing this far higher (tried up to 50,000) stops reading as
// "many individual glowing dots" (User Story 1/SC-001's actual visual requirement) and starts
// reading as a smooth, blurred, textureless surface instead — the reference image's crisp
// per-dot look comes from a moderate, not extreme, count at typical display sizes. FR-001
// (visual fidelity) takes priority over FR-009's "substantially higher" when the two pull in
// different directions, per spec.md's own priority ordering (US1 > US3). A single THREE.Points
// draw call scales cheaply to this count regardless (no per-vertex lighting, no InstancedMesh
// overhead); re-tune upward only if it still reads as discrete dots after doing so — verify in
// a real browser (quickstart.md "User Story 1"), not just this constant in isolation. 'reduced'
// keeps 010's original density (see sphereRenderTechnique.ts for the tier's simpler technique).
const POINT_COUNT_BY_TIER = { full: 8000, reduced: 500 } as const

// Corrected after live user review of the real chat page: the earlier large point size
// (0.7) made 8,000 particles overlap so heavily they merged into a smooth blob/halo instead
// of reading as individual dots — the actual product goal (matching the user-supplied
// reference image/implementation, which uses small, distinctly separated flat dots, no
// glow wash). Point size is now small enough that neighboring dots don't touch at typical
// display size. 'reduced' keeps 010's original size (normal blending, no overlap risk).
const BASE_POINT_SIZE_BY_TIER = { full: 0.12, reduced: 6 } as const
// With points no longer overlapping heavily, intensity can go back up near 1 — each dot
// should read as a fully visible, saturated color, not a faint speck. 'reduced' stays at
// 1.0 (unchanged from 010 — normal blending never needed this lever).
const INTENSITY_BY_TIER = { full: 0.95, reduced: 1 } as const

interface ReactiveSphereProps {
  /** Ref-based getter for the 0 (silent) – 1 (loud) damped TTS envelope (useTextToSpeech's
   * `getIntensity`, FR-018, research.md §3) — read every frame here rather than passed as
   * a plain number prop, so the assistant's voice doesn't force a React re-render per frame. */
  getReactiveIntensity: () => number
  /** research.md §4/§5 — 'full' uses a denser dot lattice and the glow/bloom rendering
   * technique; 'reduced' uses a lower count and a simpler technique (sphereRenderTechnique.ts);
   * 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** FR-011: freezes idle rotation/breathing and caps reactive amplitude when the user prefers reduced motion. */
  reducedMotion: boolean
  /** Optional external ref to the sphere's outer `<group>` — SceneBackground.tsx passes this
   * through to `ParticleSphereBloom`'s `selection` so only this object blooms (FR-004,
   * research.md §3). Falls back to an internal ref when omitted so this component still works
   * standalone (e.g. in isolation, without a bloom pass mounted). */
  groupRef?: RefObject<THREE.Group | null>
}

/** The workspace's abstract, audio-reactive dot-mesh sphere (spec.md Clarifications — not
 * a geographic globe; spec 011-particle-sphere-engine FR-001 — a uniform Fibonacci-distributed
 * mesh of dots, not concentric rings or a solid shaded surface). Idles via a slow noise-driven
 * wobble/rotation plus a breathing pulse (FR-006, sphereBreath.ts); deforms further while the
 * assistant is speaking (getReactiveIntensity() above zero); dot colors follow the current theme
 * (FR-008, dotMeshTheme.ts). */
export function ReactiveSphere({
  getReactiveIntensity,
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
      // Same rationale as above, for the tier-derived point size/intensity (FR-012 — a
      // performance-regression downgrade to 'reduced' must pick up its own, non-saturating
      // values immediately, not keep 'full's settings from before the downgrade).
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
