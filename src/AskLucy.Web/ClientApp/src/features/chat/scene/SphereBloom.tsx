import { useFrame } from '@react-three/fiber'
import { EffectComposer, SelectiveBloom } from '@react-three/postprocessing'
import { KernelSize, type SelectiveBloomEffect } from 'postprocessing'
import { type RefObject, useRef } from 'react'
import type { Group, Object3D } from 'three'
import type { FrequencyBands } from '../voice/useVoiceAnalyzer'

// Re-enabled 2026-09-07 (live user review — voltviz's GlowSphere reference, an open-source
// visualizer, confirmed this exact technique: THREE's UnrealBloomPass, which
// @react-three/postprocessing's SelectiveBloom below wraps). Previously disabled: the old
// particle-cloud sphere's individually crisp dots visibly "ramp up" into a blurred glow over
// the first couple of seconds after mount when `luminanceSmoothing` was set high (0.9) — a real
// bug in the effect's adaptive convergence. `luminanceSmoothing: 0` here removes that adaptive
// lag entirely (the threshold applies instantly, every frame, matching UnrealBloomPass's own
// non-adaptive behavior) rather than working around it with intensity/threshold tuning alone.
// Kept deliberately faint at rest — "distinguish the sphere from the card background," not
// voltviz's own much bigger reference glow — via low base intensity and a small blur kernel.
// Lowered further (live user report, 2026-09-07): at the original values the halo read as
// visually too strong, and — on both an RTX 3080 and an RTX 4060 — was expensive enough to
// trip the scene's own one-way performance-regression downgrade (useSceneQualityTier.ts)
// about 10 seconds after mount, silently dropping the sphere from 'full' to 'reduced' tier
// (fewer points, bloom disabled entirely) for the rest of the session. RADIUS/LEVELS below are
// the actual cost levers (fewer mipmap samples, smaller blur spread) — INTENSITY/GAIN are pure
// visual strength and don't affect render cost on their own.
const BLOOM_INTENSITY_BASE = 0.06
const BLOOM_INTENSITY_GAIN = 0.25
// Raised so fewer, only the very brightest pixels qualify for bloom at all — both a tighter
// visual glow and less work per frame (fewer pixels enter the blur passes).
const BLOOM_LUMINANCE_THRESHOLD = 0.75
const BLOOM_LUMINANCE_SMOOTHING = 0
// mipmapBlur's actual GPU cost knobs (defaults are radius 0.85, levels 8 — full-viewport hero
// demo values, not tuned for a small card). Smaller radius = tighter halo *and* less blur work;
// fewer levels = fewer mipmap samples per pixel.
const BLOOM_RADIUS = 0.35
const BLOOM_LEVELS = 4
// Same asymmetric attack/release shape as ReactiveSphere.tsx's `volume` variation (fast
// brighten, slow fade) so the glow's pulse reads as synced to the sphere's own reactivity
// rather than a second, independently-timed animation.
const VOLUME_UP_EASING = 0.03
const VOLUME_DOWN_EASING = 0.002

interface SphereBloomProps {
  /** The sphere's outer `<group>` ref (`ReactiveSphere.tsx`). `SelectiveBloom`'s `selection`
   * prop restricts the bloom pass to this object via a dedicated render layer, so nothing else
   * sharing the scene (ambient light, `OrbitControls`, anything added later) is affected
   * (spec 011-particle-sphere-engine FR-004, Clarification Q2, research.md §3). */
  sphereRef: RefObject<Group | null>
  /** Same real per-band FFT getter passed to `ReactiveSphere` — read every frame here (not a
   * React prop/state) for the same reason ReactiveSphere reads it that way: 60fps updates would
   * far exceed a sane React re-render rate. */
  getFrequencyBands: () => FrequencyBands
}

/** Scoped glow around the sphere's brightest pixels (its fresnel rim highlight) — faint at
 * rest, just enough to separate the sphere from the card background in both light and dark
 * theme, brightening with real speech volume rather than a dramatic constant halo
 * (spec 011-particle-sphere-engine FR-004). */
export function SphereBloom({ sphereRef, getFrequencyBands }: SphereBloomProps) {
  const bloomRef = useRef<SelectiveBloomEffect>(null)
  const volume = useRef(0)

  useFrame((_, delta) => {
    const bloom = bloomRef.current
    if (!bloom) return

    const deltaMs = delta * 1000
    const bands = getFrequencyBands()
    const target = Math.max(bands.low, bands.mid, bands.high)
    const easing = target > volume.current ? VOLUME_UP_EASING : VOLUME_DOWN_EASING
    volume.current += (target - volume.current) * easing * deltaMs

    bloom.intensity = BLOOM_INTENSITY_BASE + volume.current * BLOOM_INTENSITY_GAIN
  })

  return (
    <EffectComposer>
      <SelectiveBloom
        ref={bloomRef}
        // @react-three/postprocessing types `selection` as `RefObject<Object3D>` (current
        // never null), but every real R3F ref starts null until the object mounts — the
        // library's own runtime handles a null `.current` fine (it reads `.current` lazily
        // after refs are attached), this cast only works around the overly strict upstream type.
        selection={sphereRef as RefObject<Object3D>}
        // Overwritten every frame above once the effect mounts — this is just the value used
        // for the first frame or two before that.
        intensity={BLOOM_INTENSITY_BASE}
        luminanceThreshold={BLOOM_LUMINANCE_THRESHOLD}
        luminanceSmoothing={BLOOM_LUMINANCE_SMOOTHING}
        kernelSize={KernelSize.SMALL}
        radius={BLOOM_RADIUS}
        levels={BLOOM_LEVELS}
        mipmapBlur
      />
    </EffectComposer>
  )
}
