import { useFrame } from '@react-three/fiber'
import { type RefObject, useMemo, useRef } from 'react'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'
import { getDotMeshColors } from './dotMeshTheme'
import fragmentShader from './sphere.frag.glsl?raw'
import vertexShader from './sphere.vert.glsl?raw'

const SPHERE_RADIUS = 1.4

// SphereGeometry subdivision per quality tier — the reference project (organic-sphere,
// user-supplied source, 2026-09-07) uses 512x512 for a full-viewport hero demo; this sphere
// lives in a ~278px card, so both tiers are cut down heavily for cost with no visible loss of
// smoothness at that size. 'reduced' stays coarser for the mobile breakpoint.
const SEGMENTS_BY_TIER = { full: 128, reduced: 48 } as const

const TANGENT_DEFINES = { USE_TANGENT: '' } as const

// Static light directions/intensities/fresnel tuning, ported from the reference's Sphere.js
// `setMaterial()` — only the two light *colors* are theme-driven (dotMeshTheme.ts) instead of
// the reference's fixed orange/blue. FRESNEL_OFFSET is the one other deliberate deviation (see
// sphere.frag.glsl's header, live user review 2026-09-07): the reference's -1.609 clamps
// every near-head-on fragment to pure black, which reads as "no color" in this app's small
// card even though it's dramatic in the reference's full-viewport hero demo. Everything else
// here shapes the sphere's form and was tuned by the reference's author, not something this
// app has an opinion on.
const LIGHT_A_SPHERICAL = new THREE.Spherical(1, 0.615, 2.049)
const LIGHT_B_SPHERICAL = new THREE.Spherical(1, 2.561, -1.844)
const LIGHT_A_INTENSITY = 1.85
const LIGHT_B_INTENSITY = 1.4
const DISTORTION_FREQUENCY = 1.5
const DISPLACEMENT_FREQUENCY = 2.12
const FRESNEL_OFFSET = -0.8
const FRESNEL_POWER = 1.793

interface Variation {
  current: number
  upEasing: number
  downEasing: number
  getTarget: (bands: FrequencyBands) => number
}

type VariationName = 'volume' | 'lowLevel' | 'mediumLevel' | 'highLevel'

// Idle defaults for each variation, also used directly as the shader's initial uniform
// values below (so both stay in sync without reading a ref during render).
const VOLUME_DEFAULT = 0.152
const LOW_LEVEL_DEFAULT = 0.0003
const MEDIUM_LEVEL_DEFAULT = 3.587
const HIGH_LEVEL_DEFAULT = 0.65

/** Ported from the reference's `Sphere.js` `setVariations()`: four independently-eased values
 * (fast attack, slow release, each at its own speed) rather than one raw value driving
 * everything 1:1 — that per-channel lag is what gives the reference's motion its organic,
 * non-mechanical feel. Each reads its own real frequency band from `getFrequencyBands`
 * (`useVoiceAnalyzer.ts`'s Web Audio `AnalyserNode`, matching the reference's own
 * `Microphone.js`-derived bands 1:1) rather than one collapsed scalar — live user review,
 * 2026-09-07, replacing an earlier version where all four read the same single damped TTS
 * envelope. Numeric constants (defaults, easings, target scale factors) are the reference's
 * own tuning. */
function createVariations(): Record<VariationName, Variation> {
  return {
    volume: {
      current: VOLUME_DEFAULT,
      upEasing: 0.03,
      downEasing: 0.002,
      getTarget: (b) => VOLUME_DEFAULT + Math.max(b.low, b.mid, b.high) * 0.3,
    },
    lowLevel: {
      current: LOW_LEVEL_DEFAULT,
      upEasing: 0.005,
      downEasing: 0.002,
      getTarget: (b) => LOW_LEVEL_DEFAULT + b.low * 0.003,
    },
    mediumLevel: {
      current: MEDIUM_LEVEL_DEFAULT,
      upEasing: 0.008,
      downEasing: 0.004,
      getTarget: (b) => MEDIUM_LEVEL_DEFAULT + b.mid * 2,
    },
    highLevel: {
      current: HIGH_LEVEL_DEFAULT,
      upEasing: 0.02,
      downEasing: 0.001,
      getTarget: (b) => HIGH_LEVEL_DEFAULT + b.high * 5,
    },
  }
}

interface ReactiveSphereProps {
  /** Ref-based getter for real low/mid/high frequency bands (useVoiceAnalyzer's
   * `getFrequencyBands`, FR-018, research.md §3) — read every frame here rather than passed
   * as plain number props, so the assistant's voice doesn't force a React re-render per frame. */
  getFrequencyBands: () => FrequencyBands
  /** research.md §4/§5 — 'full' uses a finer SphereGeometry subdivision (SEGMENTS_BY_TIER);
   * 'reduced' uses a coarser one; 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** FR-011: freezes the noise-space drift and reactive easing when the user prefers reduced motion. */
  reducedMotion: boolean
  /** Optional external ref to the sphere's outer `<group>` — SceneBackground.tsx passes this
   * through to `ParticleSphereBloom`'s `selection` so only this object blooms (FR-004,
   * research.md §3). Falls back to an internal ref when omitted so this component still works
   * standalone (e.g. in isolation, without a bloom pass mounted). */
  groupRef?: RefObject<THREE.Group | null>
}

/** The workspace's abstract, audio-reactive organic sphere (spec.md Clarifications — not a
 * geographic globe). Ported from Bruno Simon's "Organic Sphere" reference project (2026-09-07,
 * live user review, option 2 — see sphere.vert.glsl's header for the full history of why a
 * from-scratch particle-cloud technique was replaced). A continuous noise-displaced mesh with
 * twin-light fresnel shading; surface colors follow the current theme (FR-008, dotMeshTheme.ts). */
export function ReactiveSphere({
  getFrequencyBands,
  qualityTier,
  reducedMotion,
  groupRef: externalGroupRef,
}: ReactiveSphereProps) {
  const materialRef = useRef<THREE.ShaderMaterial>(null)
  const internalGroupRef = useRef<THREE.Group>(null)
  const groupRef = externalGroupRef ?? internalGroupRef
  const mode = useThemeStore((s) => s.mode)

  const segments = SEGMENTS_BY_TIER[qualityTier]
  const geometry = useMemo(() => {
    const geo = new THREE.SphereGeometry(SPHERE_RADIUS, segments, segments)
    // The vertex shader estimates normals from `tangent`-offset neighbor samples (see
    // sphere.vert.glsl) rather than using the geometry's own interpolated normal, since the
    // noise displacement invalidates the undisplaced sphere's normals.
    geo.computeTangents()
    return geo
  }, [segments])

  const variations = useRef(createVariations())
  // A slowly, near-linearly drifting 3D "location" in the noise field (reference's `uOffset`)
  // — recomputed every frame from a spherical direction that itself barely changes per tick,
  // so the sphere's displacement pattern evolves continuously instead of repeating or holding
  // still. Ported as-is; see the reference's Sphere.js `update()` for the exact derivation.
  // The starting direction is an arbitrary fixed constant, not randomized (React's
  // render-purity rules disallow Math.random() during render) — it only affects which
  // direction the drift starts from, not the drift itself.
  const offsetSpherical = useRef(new THREE.Spherical(1, Math.PI / 3, (Math.PI * 4) / 5))
  const offsetDirection = useRef(new THREE.Vector3())

  const dotColors = getDotMeshColors(mode)

  const uniforms = useMemo(
    () => ({
      uLightAColor: { value: new THREE.Color(dotColors.idle) },
      uLightAPosition: { value: new THREE.Vector3().setFromSpherical(LIGHT_A_SPHERICAL) },
      uLightAIntensity: { value: LIGHT_A_INTENSITY },
      uLightBColor: { value: new THREE.Color(dotColors.reactive) },
      uLightBPosition: { value: new THREE.Vector3().setFromSpherical(LIGHT_B_SPHERICAL) },
      uLightBIntensity: { value: LIGHT_B_INTENSITY },
      uSubdivision: { value: new THREE.Vector2(segments, segments) },
      uOffset: { value: new THREE.Vector3() },
      uDistortionFrequency: { value: DISTORTION_FREQUENCY },
      uDistortionStrength: { value: HIGH_LEVEL_DEFAULT },
      uDisplacementFrequency: { value: DISPLACEMENT_FREQUENCY },
      uDisplacementStrength: { value: VOLUME_DEFAULT },
      uFresnelOffset: { value: FRESNEL_OFFSET },
      uFresnelMultiplier: { value: MEDIUM_LEVEL_DEFAULT },
      uFresnelPower: { value: FRESNEL_POWER },
      uTime: { value: 0 },
    }),
    // Initial values only — mode changes are applied to the existing uniforms in useFrame
    // below instead of recreating the material on every theme toggle.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [segments],
  )

  useFrame((_, delta) => {
    const material = materialRef.current
    if (!material) return
    const u = material.uniforms as typeof uniforms

    if (!reducedMotion) {
      // Reference's Time.js reports delta in milliseconds and its easing/frequency constants
      // were tuned against that; R3F's `delta` is in seconds, so convert to keep the exact
      // same tuning.
      const deltaMs = delta * 1000
      const bands = getFrequencyBands()

      const v = variations.current
      for (const key of Object.keys(v) as VariationName[]) {
        const variation = v[key]
        const target = variation.getTarget(bands)
        const easing = target > variation.current ? variation.upEasing : variation.downEasing
        variation.current += (target - variation.current) * easing * deltaMs
      }

      const timeFrequency = v.lowLevel.current
      const elapsedTime = deltaMs * timeFrequency

      u.uDisplacementStrength.value = v.volume.current
      u.uDistortionStrength.value = v.highLevel.current
      u.uFresnelMultiplier.value = v.mediumLevel.current

      const offsetTime = elapsedTime * 0.3
      const spherical = offsetSpherical.current
      spherical.phi = ((Math.sin(offsetTime * 0.001) * Math.sin(offsetTime * 0.00321)) * 0.5 + 0.5) * Math.PI
      spherical.theta = ((Math.sin(offsetTime * 0.0001) * Math.sin(offsetTime * 0.000321)) * 0.5 + 0.5) * Math.PI * 2
      offsetDirection.current.setFromSpherical(spherical).multiplyScalar(timeFrequency * 2)
      u.uOffset.value.add(offsetDirection.current)

      u.uTime.value += elapsedTime
    }

    // FR-008/SC-004: write the current theme's colors into the uniforms in place every
    // frame (cheap — two Color.set calls) rather than recreating the material on theme
    // toggle, so the switch applies with no perceptible delay/flash.
    u.uLightAColor.value.set(dotColors.idle)
    u.uLightBColor.value.set(dotColors.reactive)
  })

  return (
    <group ref={groupRef}>
      <mesh geometry={geometry}>
        <shaderMaterial
          ref={materialRef}
          vertexShader={vertexShader}
          fragmentShader={fragmentShader}
          uniforms={uniforms}
          defines={TANGENT_DEFINES}
        />
      </mesh>
    </group>
  )
}
