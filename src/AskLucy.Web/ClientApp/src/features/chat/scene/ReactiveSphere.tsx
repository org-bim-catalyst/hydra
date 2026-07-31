import { useFrame } from '@react-three/fiber'
import { useMemo, useRef } from 'react'
import { createNoise2D } from 'simplex-noise'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
import { getDotMeshColors } from './dotMeshTheme'
import fragmentShader from './sphere.frag.glsl?raw'
import vertexShader from './sphere.vert.glsl?raw'

const IDLE_AMPLITUDE = 0.06
const IDLE_FREQUENCY = 1.4
const REACTIVE_AMPLITUDE_MAX = 0.35
const REACTIVE_FREQUENCY_MAX = 2.2
const IDLE_ROTATION_SPEED = 0.08 // rad/s
const SPHERE_RADIUS = 1.4
const BASE_POINT_SIZE = 6

// spec 010-lucy-brand-refresh FR-006/FR-009, tasks.md T016: point counts per quality tier —
// dense enough on 'full' to read as a continuous dotted mesh, thinned on 'reduced' rather
// than disappearing.
const POINT_COUNT_BY_TIER = { full: 1400, reduced: 500 } as const

/** Samples points on a unit sphere as concentric latitude rings (spec 010-lucy-brand-refresh
 * FR-006 — "concentric/orbital ring patterns", research.md §1) rather than a uniform random
 * scatter, so the dot mesh visually reads as rings, not noise. Ring count derives from the
 * requested total so density stays roughly even pole-to-equator (more points on wider,
 * near-equator rings). */
function generateRingSpherePositions(totalPoints: number, radius: number): Float32Array {
  const ringCount = Math.max(6, Math.round(Math.sqrt((totalPoints * Math.PI) / 2)))
  const positions: number[] = []

  for (let ring = 0; ring < ringCount; ring++) {
    const v = (ring + 0.5) / ringCount // 0..1, offset half-step to avoid a pole singularity
    const polarAngle = v * Math.PI
    const ringRadius = Math.sin(polarAngle)
    const y = Math.cos(polarAngle)

    const pointsInRing = Math.max(3, Math.round(ringCount * 2 * ringRadius))
    for (let p = 0; p < pointsInRing; p++) {
      const theta = (p / pointsInRing) * Math.PI * 2
      positions.push(
        Math.cos(theta) * ringRadius * radius,
        y * radius,
        Math.sin(theta) * ringRadius * radius,
      )
    }
  }

  return new Float32Array(positions)
}

interface ReactiveSphereProps {
  /** Ref-based getter for the 0 (silent) – 1 (loud) damped TTS envelope (useTextToSpeech's
   * `getIntensity`, FR-018, research.md §3) — read every frame here rather than passed as
   * a plain number prop, so the assistant's voice doesn't force a React re-render per frame. */
  getReactiveIntensity: () => number
  /** research.md §4 — 'full' uses a denser dot lattice than 'reduced'; 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** FR-012: freezes idle rotation and caps reactive amplitude when the user prefers reduced motion. */
  reducedMotion: boolean
}

/** The workspace's abstract, audio-reactive dot-mesh sphere (spec.md Clarifications — not
 * a geographic globe; spec 010-lucy-brand-refresh FR-006/007 — a mesh of dots in
 * concentric rings, not a solid shaded surface). Idles via a slow noise-driven wobble/
 * rotation; deforms further while the assistant is speaking (getReactiveIntensity() above
 * zero); dot colors follow the current theme (FR-008, dotMeshTheme.ts). */
export function ReactiveSphere({
  getReactiveIntensity,
  qualityTier,
  reducedMotion,
}: ReactiveSphereProps) {
  const materialRef = useRef<THREE.ShaderMaterial>(null)
  const groupRef = useRef<THREE.Group>(null)
  const wobbleNoise = useMemo(() => createNoise2D(), [])
  const elapsed = useRef(0)
  const mode = useThemeStore((s) => s.mode)

  const positions = useMemo(
    () => generateRingSpherePositions(POINT_COUNT_BY_TIER[qualityTier], SPHERE_RADIUS),
    [qualityTier],
  )

  const dotColors = getDotMeshColors(mode)

  const uniforms = useMemo(
    () => ({
      uTime: { value: 0 },
      uAmplitude: { value: IDLE_AMPLITUDE },
      uFrequency: { value: IDLE_FREQUENCY },
      uBasePointSize: { value: BASE_POINT_SIZE },
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
      } else {
        // FR-012: reduced motion freezes both continuous rotation and reactive growth —
        // the noise pattern holds still rather than continuing to animate.
        u.uAmplitude.value = IDLE_AMPLITUDE
        u.uFrequency.value = IDLE_FREQUENCY
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
        />
      </points>
    </group>
  )
}
