import { EffectComposer, SelectiveBloom } from '@react-three/postprocessing'
import { KernelSize } from 'postprocessing'
import type { RefObject } from 'react'
import type { Group, Object3D } from 'three'

// Re-enabled 2026-09-07 (live user review — voltviz's GlowSphere reference, an open-source
// visualizer, confirmed this exact technique: THREE's UnrealBloomPass, which
// @react-three/postprocessing's SelectiveBloom below wraps). Previously disabled: the old
// particle-cloud sphere's individually crisp dots visibly "ramp up" into a blurred glow over
// the first couple of seconds after mount when `luminanceSmoothing` was set high (0.9) — a real
// bug in the effect's adaptive convergence. `luminanceSmoothing: 0` here removes that adaptive
// lag entirely (the threshold applies instantly, every frame, matching UnrealBloomPass's own
// non-adaptive behavior) rather than working around it with intensity/threshold tuning alone.
// Kept deliberately faint — "distinguish the sphere from the card background," not voltviz's
// own much bigger reference glow — via low intensity and a small blur kernel.
const BLOOM_INTENSITY = 0.15
const BLOOM_LUMINANCE_THRESHOLD = 0.6
const BLOOM_LUMINANCE_SMOOTHING = 0

interface SphereBloomProps {
  /** The sphere's outer `<group>` ref (`ReactiveSphere.tsx`). `SelectiveBloom`'s `selection`
   * prop restricts the bloom pass to this object via a dedicated render layer, so nothing else
   * sharing the scene (ambient light, `OrbitControls`, anything added later) is affected
   * (spec 011-particle-sphere-engine FR-004, Clarification Q2, research.md §3). */
  sphereRef: RefObject<Group | null>
}

/** Scoped, deliberately subtle glow around the sphere's brightest pixels (its fresnel rim
 * highlight) — just enough to separate it from the card background in both light and dark
 * theme, not a dramatic halo (spec 011-particle-sphere-engine FR-004). */
export function SphereBloom({ sphereRef }: SphereBloomProps) {
  return (
    <EffectComposer>
      <SelectiveBloom
        // @react-three/postprocessing types `selection` as `RefObject<Object3D>` (current
        // never null), but every real R3F ref starts null until the object mounts — the
        // library's own runtime handles a null `.current` fine (it reads `.current` lazily
        // after refs are attached), this cast only works around the overly strict upstream type.
        selection={sphereRef as RefObject<Object3D>}
        intensity={BLOOM_INTENSITY}
        luminanceThreshold={BLOOM_LUMINANCE_THRESHOLD}
        luminanceSmoothing={BLOOM_LUMINANCE_SMOOTHING}
        kernelSize={KernelSize.SMALL}
        mipmapBlur
      />
    </EffectComposer>
  )
}
