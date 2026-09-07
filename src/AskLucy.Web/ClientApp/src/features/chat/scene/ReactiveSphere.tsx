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
// No longer audio-reactive (see the displacement-simplification block below) — voltviz's own
// fragment shader has no lighting/fresnel concept at all to react in the first place, so a
// fixed value here matches the reference at least as closely as the previous audio-driven one.
const FRESNEL_MULTIPLIER = 3.587
// Blinn-Phong specular tuning (live user review, 2026-09-07 — "instead of metal make it
// glass") — see sphere.frag.glsl's header for why these are now white rather than light-tinted.
// Shininess raised further and strength boosted from the earlier "metallic" pass for small,
// crisp, bright glints — a glass surface's specular reads as sharper and more intense than a
// brushed-metal one.
const SPECULAR_SHININESS = 90
const SPECULAR_STRENGTH = 0.9

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
// VOLUME_LIGHTNESS_GAIN lives in the displacement-simplification const block below — it ties
// this hue-rotation system to the same single audio scalar that drives the sphere's shape.

function currentHueDegrees(elapsedSeconds: number): number {
  return ((elapsedSeconds / HUE_ROTATION_PERIOD_SECONDS) % 1) * 360
}

// Single-formula displacement (live user review, 2026-09-07 — A/B test against an earlier
// 4-independently-eased-variation version, ported from Bruno Simon's "Organic Sphere"; see git
// history for that version and sphere.vert.glsl's header for the reasoning). Mirrors voltviz's
// GlowSphere structure: one averaged loudness value, no extra JS-side smoothing beyond the
// AnalyserNode's own default `smoothingTimeConstant` (0.8 — voltviz sets this explicitly;
// useVoiceAnalyzer.ts already uses the Web Audio default of the same value, so no change was
// needed there), one noise call, one scale factor.
const NOISE_FREQUENCY = 2.12
// A permanent noise-driven baseline, independent of audio — live user feedback: a sphere that
// goes fully flat/smooth at silence (voltviz's own zero-average behavior, and this file's
// previous version) reads as "dead" rather than "calm". This keeps a gentle, continuous wobble
// at rest; uAudioLevel/uDisplacementScale below add the louder, more energetic "voltviz when
// speaking" reactivity on top of it.
const IDLE_DISPLACEMENT = 0.15
// Raised to 0.9 for a punchier "voltviz when speaking" reaction, then brought back down to 0.5
// (live user feedback: 0.9 was too strong). audioLevel(0..1) times this is the sphere's
// *additional* displacement at full volume, on top of IDLE_DISPLACEMENT above.
const DISPLACEMENT_SCALE = 0.5
// How fast the noise pattern itself evolves over time, independent of audio — voltviz's
// equivalent is `elapsed * settings.speed`.
const TIME_SPEED = 0.3
// How much louder speech brightens both lights (HSL lightness) on top of their theme-mode base
// — unchanged in spirit from the previous version, just reading the same single audioLevel now
// instead of a separately-eased `volume` variation.
const VOLUME_LIGHTNESS_GAIN = 0.6

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
   * through to `SphereBloom`'s `selection` so only this object blooms (FR-004,
   * research.md §3). Falls back to an internal ref when omitted so this component still works
   * standalone (e.g. in isolation, without a bloom pass mounted). */
  groupRef?: RefObject<THREE.Group | null>
}

/** The workspace's abstract, audio-reactive organic sphere (spec.md Clarifications — not a
 * geographic globe). Ported from Bruno Simon's "Organic Sphere" reference project (2026-09-07,
 * live user review, option 2 — see sphere.vert.glsl's header for the full history of why a
 * from-scratch particle-cloud technique was replaced). A continuous noise-displaced mesh with
 * twin-light fresnel shading; light colors slowly cycle through a preset hue palette
 * (HUE_CYCLE_DEGREES above, vizz.fm-inspired, live user review 2026-09-07) with lightness
 * tuned per theme mode and boosted by real speech volume. */
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

  // Single audio-reactive scalar (0 silent – 1 loud), set directly from the loudest real FFT
  // band each frame with no extra JS-side easing — see the const block above for why.
  const audioLevel = useRef(0)
  // Real elapsed seconds — both uTime's noise evolution and currentHueDegrees() cycle against
  // this same wall-clock now that there's no separate audio-scaled "noise time" concept.
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
      uFrequency: { value: NOISE_FREQUENCY },
      uAudioLevel: { value: 0 },
      uDisplacementScale: { value: DISPLACEMENT_SCALE },
      uIdleDisplacement: { value: IDLE_DISPLACEMENT },
      uFresnelOffset: { value: FRESNEL_OFFSET },
      uFresnelMultiplier: { value: FRESNEL_MULTIPLIER },
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
      audioLevel.current = Math.max(bands.low, bands.mid, bands.high)
      u.uAudioLevel.value = audioLevel.current

      elapsedSeconds.current += delta
      u.uTime.value = elapsedSeconds.current * TIME_SPEED
    }

    // Runs every frame regardless of reducedMotion (unlike the block above) so the lights are
    // always set to a real color — reducedMotion only stops elapsedSeconds from advancing, it
    // doesn't skip color assignment, which would otherwise leave both lights at their
    // THREE.Color() default (black) for a user with reduced motion enabled.
    const hueDegrees = currentHueDegrees(elapsedSeconds.current)
    const hue01 = hueDegrees / 360
    const lightnessBoost = audioLevel.current * VOLUME_LIGHTNESS_GAIN
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
