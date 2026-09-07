import * as THREE from 'three'

export interface SphereRenderTechnique {
  blending: THREE.Blending
  bloomEnabled: boolean
}

/** Derives the particle sphere's rendering technique from its quality tier (spec
 * 011-particle-sphere-engine FR-010, Clarification Q3, research.md §5). The "full" tier gets
 * additive blending (research.md §2 — overlap brightening) plus the scoped bloom pass
 * (`ParticleSphereBloom`); "reduced" deliberately keeps a simpler, non-glow technique — the
 * same shared geometry/animation, just without the two most GPU-costly parts — rather than
 * the "full" technique at a lower particle count. */
export function getSphereRenderTechnique(qualityTier: 'full' | 'reduced'): SphereRenderTechnique {
  if (qualityTier === 'full') {
    return { blending: THREE.AdditiveBlending, bloomEnabled: true }
  }

  return { blending: THREE.NormalBlending, bloomEnabled: false }
}
