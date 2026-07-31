import { useFrame } from '@react-three/fiber'
import { useMemo, useRef } from 'react'
import { createNoise2D } from 'simplex-noise'
import * as THREE from 'three'
import fragmentShader from './sphere.frag.glsl?raw'
import vertexShader from './sphere.vert.glsl?raw'

const IDLE_AMPLITUDE = 0.06
const IDLE_FREQUENCY = 1.4
const REACTIVE_AMPLITUDE_MAX = 0.35
const REACTIVE_FREQUENCY_MAX = 2.2
const IDLE_ROTATION_SPEED = 0.08 // rad/s

interface ReactiveSphereProps {
  /** Ref-based getter for the 0 (silent) – 1 (loud) damped TTS envelope (useTextToSpeech's
   * `getIntensity`, FR-018, research.md §3) — read every frame here rather than passed as
   * a plain number prop, so the assistant's voice doesn't force a React re-render per frame. */
  getReactiveIntensity: () => number
  /** research.md §4 — 'full' uses higher geometry detail than 'reduced'; 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** FR-012: freezes idle rotation and caps reactive amplitude when the user prefers reduced motion. */
  reducedMotion: boolean
}

/** The workspace's abstract, audio-reactive 3D sphere (spec.md Clarifications — not a
 * geographic globe). Idles via a slow noise-driven wobble/rotation; deforms further
 * while the assistant is speaking (getReactiveIntensity() above zero). */
export function ReactiveSphere({
  getReactiveIntensity,
  qualityTier,
  reducedMotion,
}: ReactiveSphereProps) {
  const materialRef = useRef<THREE.ShaderMaterial>(null)
  const groupRef = useRef<THREE.Group>(null)
  const wobbleNoise = useMemo(() => createNoise2D(), [])
  const elapsed = useRef(0)

  const detail = qualityTier === 'full' ? 4 : 2

  const uniforms = useMemo(
    () => ({
      uTime: { value: 0 },
      uAmplitude: { value: IDLE_AMPLITUDE },
      uFrequency: { value: IDLE_FREQUENCY },
      uColorIdle: { value: new THREE.Color('#1F4E5E') },
      uColorReactive: { value: new THREE.Color('#B8461F') },
    }),
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
    }

    if (groupRef.current && !reducedMotion) {
      const wobble = wobbleNoise(elapsed.current * 0.05, 0)
      groupRef.current.rotation.y += (IDLE_ROTATION_SPEED + wobble * 0.02) * delta
    }
  })

  return (
    <group ref={groupRef}>
      <mesh>
        <icosahedronGeometry args={[1.4, detail]} />
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
