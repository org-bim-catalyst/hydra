import { useFrame } from '@react-three/fiber'
import { type RefObject, useMemo, useRef } from 'react'
import * as THREE from 'three'
import { useThemeStore } from '../../../store/themeStore'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'
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
// `setMaterial()`. FRESNEL_OFFSET is one deliberate deviation (see sphere.frag.glsl's header,
// live user review 2026-09-07): the reference's -1.609 clamps every near-head-on fragment to
// pure black, which reads as "no color" in this app's small card even though it's dramatic in
// the reference's full-viewport hero demo. The two light *colors* are a second deviation — see
// the hue-cycling block below — everything else here shapes the sphere's form and was tuned by
// the reference's author, not something this app has an opinion on.
const LIGHT_A_SPHERICAL = new THREE.Spherical(1, 0.615, 2.049)
const LIGHT_B_SPHERICAL = new THREE.Spherical(1, 2.561, -1.844)
const LIGHT_A_INTENSITY = 1.85
const LIGHT_B_INTENSITY = 1.4
const FRESNEL_OFFSET = -0.8
const FRESNEL_POWER = 1.793
// Blinn-Phong specular tuning ("silver metal", live user review, 2026-09-07 — see
// sphere.frag.glsl's header for the brief "glass" experiment reverted here). Shininess this
// high keeps the highlight small and tight (polished metal), not a soft plastic sheen; strength
// is deliberately modest so it reads as a glint, not a wash.
const SPECULAR_SHININESS = 48
const SPECULAR_STRENGTH = 0.6

// Continuous full-spectrum hue rotation ("rotating RGB", live user request 2026-09-07) —
// replaces an earlier version that crossfaded between four hand-picked preset hues (vizz.fm's
// "Polar Curves"-inspired look) with a smooth, unbroken cycle through the entire color wheel
// instead of stepping between a handful of chosen colors.
const HUE_ROTATION_PERIOD_SECONDS = 45 // one full 360° loop
const SATURATION = 0.85
// Per-mode base lightness for the two lights — dark mode can run lighter/more vivid; light
// mode needs to stay darker for contrast against a light card background (same reasoning
// dotMeshTheme.ts used before this hue-cycling system replaced it). Light B stays the
// brighter "highlight" of the pair, same relationship the old idle/reactive colors had.
const LIGHT_A_LIGHTNESS_BY_MODE = { dark: 0.55, light: 0.32 } as const
const LIGHT_B_LIGHTNESS_BY_MODE = { dark: 0.8, light: 0.5 } as const
// How much louder speech brightens both lights on top of their theme-mode base lightness —
// reads the same overall loudness (max of the three bands) as the displacement/fresnel
// reactivity below.
const VOLUME_LIGHTNESS_GAIN = 0.6

function currentHueDegrees(elapsedSeconds: number): number {
  return ((elapsedSeconds / HUE_ROTATION_PERIOD_SECONDS) % 1) * 360
}

// Redesigned 2026-09-07 (after a full day of live-tested lessons — see sphere.vert.glsl's
// header for the fuller history) to combine what actually worked: the reference's two-stage
// noise (distortion pre-pass, then displacement — richer/more organic than a single noise call
// scaled by one number, confirmed by direct A/B comparison) driven by real, *separate* FFT
// bands (useVoiceAnalyzer.ts) rather than one collapsed scalar or a four-variable
// independently-eased system. No extra JS-side smoothing beyond the AnalyserNode's own default
// smoothingTimeConstant (0.8) — kept simple on purpose.
const DISTORTION_FREQUENCY = 1.5
const DISPLACEMENT_FREQUENCY = 2.12
// Permanent baselines, independent of audio — live user feedback: a sphere that goes fully
// flat/smooth at silence reads as "dead" rather than "calm". These keep a gentle, continuous
// wobble at rest; the *_GAIN constants below add louder, more energetic reactivity on top.
const IDLE_DISTORTION = 0.3
const IDLE_DISPLACEMENT = 0.15
// First-pass tuning guesses for the redesign, deliberately modest given how many rounds of
// "too strong" earlier displacement values took to walk back — start conservative and let live
// testing say whether either needs raising.
const DISTORTION_GAIN = 0.6 // bands.low * this, added to IDLE_DISTORTION
const DISPLACEMENT_GAIN = 0.3 // bands.high * this, added to IDLE_DISPLACEMENT
const FRESNEL_MULTIPLIER_BASE = 3.587 // reference's own default
const FRESNEL_MULTIPLIER_GAIN = 1.5 // bands.mid * this, brightens the rim with mid-range speech
// How fast the noise pattern itself evolves over time, independent of audio.
const TIME_SPEED = 0.3

interface ReactiveSphereProps {
  /** Ref-based getter for real low/mid/high frequency bands (useVoiceAnalyzer's
   * `getFrequencyBands`, FR-018, research.md §3) — read every frame here rather than passed
   * as plain number props, so the assistant's voice doesn't force a React re-render per frame. */
  getFrequencyBands: () => FrequencyBands
  /** research.md §4/§5 — 'full' uses a finer SphereGeometry subdivision (SEGMENTS_BY_TIER);
   * 'reduced' uses a coarser one; 'static-fallback' never mounts this component. */
  qualityTier: 'full' | 'reduced'
  /** FR-011: freezes the noise-space evolution and reactive levels when the user prefers reduced motion. */
  reducedMotion: boolean
  /** Optional external ref to the sphere's outer `<group>` — SceneBackground.tsx passes this
   * through to `SphereBloom`'s `selection` so only this object blooms (FR-004,
   * research.md §3). Falls back to an internal ref when omitted so this component still works
   * standalone (e.g. in isolation, without a bloom pass mounted). */
  groupRef?: RefObject<THREE.Group | null>
}

/** The workspace's abstract, audio-reactive organic sphere (spec.md Clarifications — not a
 * geographic globe). A continuous two-stage noise-displaced mesh with twin-light fresnel
 * shading, redesigned 2026-09-07 to drive distortion/displacement/fresnel from real, separate
 * low/mid/high FFT bands (see the const block above); light colors continuously cycle through
 * the full hue spectrum with lightness tuned per theme mode and boosted by overall speech volume. */
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

  // Overall loudness (max of the three bands), used for the lights' lightness boost below —
  // kept as a ref (not a local variable) so it holds its last value across frames while
  // reducedMotion is true, the same way the reactive uniforms below do.
  const overallLevel = useRef(0)
  const elapsedSeconds = useRef(0)

  const uniforms = useMemo(
    () => ({
      uLightAColor: { value: new THREE.Color() },
      uLightAPosition: { value: new THREE.Vector3().setFromSpherical(LIGHT_A_SPHERICAL) },
      uLightAIntensity: { value: LIGHT_A_INTENSITY },
      uLightBColor: { value: new THREE.Color() },
      uLightBPosition: { value: new THREE.Vector3().setFromSpherical(LIGHT_B_SPHERICAL) },
      uLightBIntensity: { value: LIGHT_B_INTENSITY },
      uSubdivision: { value: new THREE.Vector2(segments, segments) },
      uDistortionFrequency: { value: DISTORTION_FREQUENCY },
      uDistortionLevel: { value: IDLE_DISTORTION },
      uDisplacementFrequency: { value: DISPLACEMENT_FREQUENCY },
      uDisplacementLevel: { value: IDLE_DISPLACEMENT },
      uFresnelOffset: { value: FRESNEL_OFFSET },
      uFresnelMultiplier: { value: FRESNEL_MULTIPLIER_BASE },
      uFresnelPower: { value: FRESNEL_POWER },
      uSpecularShininess: { value: SPECULAR_SHININESS },
      uSpecularStrength: { value: SPECULAR_STRENGTH },
      uTime: { value: 0 },
    }),
    // Initial values only — colors/theme mode are applied to the existing uniforms in
    // useFrame below instead of recreating the material every frame/toggle.
    [segments],
  )

  useFrame((_, delta) => {
    const material = materialRef.current
    if (!material) return
    const u = material.uniforms as typeof uniforms

    if (!reducedMotion) {
      const bands = getFrequencyBands()
      overallLevel.current = Math.max(bands.low, bands.mid, bands.high)

      u.uDistortionLevel.value = IDLE_DISTORTION + bands.low * DISTORTION_GAIN
      u.uDisplacementLevel.value = IDLE_DISPLACEMENT + bands.high * DISPLACEMENT_GAIN
      u.uFresnelMultiplier.value = FRESNEL_MULTIPLIER_BASE + bands.mid * FRESNEL_MULTIPLIER_GAIN

      elapsedSeconds.current += delta
      u.uTime.value = elapsedSeconds.current * TIME_SPEED
    }

    // Runs every frame regardless of reducedMotion (unlike the block above) so the lights are
    // always set to a real color — reducedMotion only stops elapsedSeconds from advancing, it
    // doesn't skip color assignment, which would otherwise leave both lights at their
    // THREE.Color() default (black) for a user with reduced motion enabled.
    const hueDegrees = currentHueDegrees(elapsedSeconds.current)
    const hue01 = hueDegrees / 360
    const lightnessBoost = overallLevel.current * VOLUME_LIGHTNESS_GAIN
    const lightnessA = THREE.MathUtils.clamp(LIGHT_A_LIGHTNESS_BY_MODE[mode] + lightnessBoost, 0, 1)
    const lightnessB = THREE.MathUtils.clamp(LIGHT_B_LIGHTNESS_BY_MODE[mode] + lightnessBoost, 0, 1)
    u.uLightAColor.value.setHSL(hue01, SATURATION, lightnessA)
    u.uLightBColor.value.setHSL(hue01, SATURATION, lightnessB)
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
