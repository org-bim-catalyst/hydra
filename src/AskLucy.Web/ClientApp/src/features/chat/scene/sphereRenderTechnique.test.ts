import * as THREE from 'three'
import { describe, expect, it } from 'vitest'
import { getSphereRenderTechnique } from './sphereRenderTechnique'

describe('getSphereRenderTechnique', () => {
  it("returns additive blending and bloom enabled for the 'full' tier", () => {
    expect(getSphereRenderTechnique('full')).toEqual({
      blending: THREE.AdditiveBlending,
      bloomEnabled: true,
    })
  })

  it("returns normal blending and bloom disabled for the 'reduced' tier (FR-010 — a simpler, non-glow technique)", () => {
    expect(getSphereRenderTechnique('reduced')).toEqual({
      blending: THREE.NormalBlending,
      bloomEnabled: false,
    })
  })
})
