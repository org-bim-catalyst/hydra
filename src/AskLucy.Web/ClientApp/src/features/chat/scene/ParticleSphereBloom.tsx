import { EffectComposer, SelectiveBloom } from '@react-three/postprocessing'
import type { RefObject } from 'react'
import type { Group, Object3D } from 'three'

// Corrected after live user review: bloom's blur radius is a fixed screen-space size, not
// tied to individual particle size, so even after shrinking dots to be individually crisp
// (ReactiveSphere.tsx), a moderate bloom pass still smeared them back together into a blob —
// the opposite of the reference image/implementation's look (small, distinct, unblurred
// dots). Kept intentionally faint — just enough to lift the brightest highlights — rather
// than removed outright, so the effect and its scoping (FR-004) stay in place for later
// tuning if a subtle touch of glow is wanted once the base dot rendering is confirmed right.
const BLOOM_INTENSITY = 0.08
const BLOOM_LUMINANCE_THRESHOLD = 0.75
const BLOOM_LUMINANCE_SMOOTHING = 0.9

interface ParticleSphereBloomProps {
  /** The particle sphere's outer `<group>` ref (`ReactiveSphere.tsx`). `SelectiveBloom`'s
   * `selection` prop restricts the bloom pass to this object via a dedicated render layer, so
   * nothing else sharing the scene (ambient light, `OrbitControls`, anything added later) is
   * affected (spec 011-particle-sphere-engine FR-004, Clarification Q2, research.md §3). */
  sphereRef: RefObject<Group | null>
}

/** Scoped neon glow for the particle sphere (spec 011-particle-sphere-engine FR-004). Mounted
 * by `SceneBackground.tsx` only for the "full" quality tier (`sphereRenderTechnique.ts`) — the
 * "reduced" tier's simpler technique never mounts this. */
export function ParticleSphereBloom({ sphereRef }: ParticleSphereBloomProps) {
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
        mipmapBlur
      />
    </EffectComposer>
  )
}
