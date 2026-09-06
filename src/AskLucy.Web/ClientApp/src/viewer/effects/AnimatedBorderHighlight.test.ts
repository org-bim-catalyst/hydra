import { describe, expect, it } from 'vitest'
import * as THREE from 'three'
import { createAnimatedBorderHighlight, type LocalPoint } from './AnimatedBorderHighlight'

const squareRing: LocalPoint[] = [
  { x: -5, y: -5 },
  { x: 5, y: -5 },
  { x: 5, y: 5 },
  { x: -5, y: 5 },
  { x: -5, y: -5 },
]

function findBorderRing(highlight: ReturnType<typeof createAnimatedBorderHighlight>) {
  // The border ring is the only THREE.Mesh in the group — everything else (the static perimeter,
  // the checkpoint shockwave) is a THREE.Line, so this is an unambiguous discriminator.
  return highlight.object3D.children.find((c) => c instanceof THREE.Mesh) as THREE.Mesh | undefined
}

function findStaticLine(highlight: ReturnType<typeof createAnimatedBorderHighlight>) {
  return highlight.object3D.children.find(
    (c) => c instanceof THREE.Line && c.material instanceof THREE.LineDashedMaterial,
  ) as THREE.Line
}

function findShockwaveLines(highlight: ReturnType<typeof createAnimatedBorderHighlight>) {
  // LineDashedMaterial extends LineBasicMaterial, so the static perimeter line would also match
  // a bare `instanceof THREE.LineBasicMaterial` check — excluded explicitly.
  return highlight.object3D.children.filter(
    (c) =>
      c instanceof THREE.Line &&
      c.material instanceof THREE.LineBasicMaterial &&
      !(c.material instanceof THREE.LineDashedMaterial),
  ) as THREE.Line[]
}

describe('createAnimatedBorderHighlight', () => {
  it('takes any ordered point list, not a hardcoded rectangle — a pentagon ring builds without error', () => {
    const pentagon: LocalPoint[] = [
      { x: 0, y: 10 },
      { x: 9, y: 3 },
      { x: 5, y: -8 },
      { x: -5, y: -8 },
      { x: -9, y: 3 },
      { x: 0, y: 10 },
    ]
    expect(() => createAnimatedBorderHighlight(pentagon, 'high')).not.toThrow()
  })

  it('renders a rotating border ring plus a checkpoint shockwave for high confidence, and hides the static line', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    expect(findBorderRing(highlight)).toBeDefined()
    expect(findStaticLine(highlight).visible).toBe(false)
    expect(findShockwaveLines(highlight)).toHaveLength(3) // core + 2 additive halos
  })

  it('renders a slower, dimmer border ring for medium confidence (FR-006 visual distinction)', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'medium')
    const ring = findBorderRing(highlight) as THREE.Mesh
    const material = ring.material as THREE.ShaderMaterial
    expect(material.uniforms.uOpacity.value).toBeLessThan(1)

    highlight.update(3) // high's 3s rotation period would complete a full loop (back near 0); medium's (6s) should not
    expect(material.uniforms.uRotation.value).toBeCloseTo(0.5, 5)
  })

  it('renders no border ring and no activation for low confidence — static, muted dashed perimeter only', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'low')
    expect(findBorderRing(highlight)).toBeUndefined()
    expect(findShockwaveLines(highlight)).toHaveLength(0)
    const staticLine = findStaticLine(highlight)
    expect(staticLine.visible).toBe(true)
    expect((staticLine.material as THREE.LineDashedMaterial).gapSize).toBeGreaterThan(0)
  })

  it('setConfidenceLevel rebuilds the border ring (and static line visibility) without needing a new instance', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    expect(findBorderRing(highlight)).toBeDefined()

    highlight.setConfidenceLevel('low')
    expect(findBorderRing(highlight)).toBeUndefined()
    expect(findStaticLine(highlight).visible).toBe(true)

    highlight.setConfidenceLevel('high')
    expect(findBorderRing(highlight)).toBeDefined()
    expect(findStaticLine(highlight).visible).toBe(false)
  })

  it('update() advances the border ring rotation each frame', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    const material = (findBorderRing(highlight) as THREE.Mesh).material as THREE.ShaderMaterial
    const rotationBefore = material.uniforms.uRotation.value

    highlight.update(0.5)

    expect(material.uniforms.uRotation.value).not.toBe(rotationBefore)
  })

  it('update() recomputes the border ring half-width from live metersPerPixel, so it reads a constant screen width across zoom', () => {
    // Regression coverage: a fixed real-world half-width (even one scaled to the boundary's own
    // size) only looks right at one particular zoom — live-verified on an actual park, where the
    // ring was only visible zoomed in. uHalfWidth must track metersPerPixel every frame instead.
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    const material = (findBorderRing(highlight) as THREE.Mesh).material as THREE.ShaderMaterial

    highlight.update(0, 2) // zoomed out: 2 metres per screen pixel
    const halfWidthZoomedOut = material.uniforms.uHalfWidth.value

    highlight.update(0, 0.1) // zoomed in: 0.1 metres per screen pixel
    const halfWidthZoomedIn = material.uniforms.uHalfWidth.value

    expect(halfWidthZoomedOut).toBeGreaterThan(halfWidthZoomedIn)
    // Targets a fixed screen-pixel width: half-width scales linearly with metersPerPixel.
    expect(halfWidthZoomedOut / halfWidthZoomedIn).toBeCloseTo(2 / 0.1, 5)
  })

  it('update() without metersPerPixel leaves the half-width at its fallback value (tests, pre-camera frames)', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    const material = (findBorderRing(highlight) as THREE.Mesh).material as THREE.ShaderMaterial
    const halfWidthBefore = material.uniforms.uHalfWidth.value

    highlight.update(0.5)

    expect(material.uniforms.uHalfWidth.value).toBe(halfWidthBefore)
  })

  it('dispose() removes all children and does not throw', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    expect(() => highlight.dispose()).not.toThrow()
    expect(highlight.object3D.children).toHaveLength(0)
  })

  describe('checkpoint activation (medium/high only)', () => {
    it('starts the border ring faded out, so the activation shockwave arrives first', () => {
      const highlight = createAnimatedBorderHighlight(squareRing, 'high')
      const ring = findBorderRing(highlight) as THREE.Mesh
      expect((ring.material as THREE.ShaderMaterial).uniforms.uIntro.value).toBe(0)
    })

    it('starts the shockwave collapsed at the centroid, not already full size', () => {
      const highlight = createAnimatedBorderHighlight(squareRing, 'high')
      const [core] = findShockwaveLines(highlight)
      expect(core.scale.x).toBeLessThan(0.1)
    })

    it('grows the shockwave and fades the border ring in as update() advances', () => {
      const highlight = createAnimatedBorderHighlight(squareRing, 'high')
      const scaleBefore = findShockwaveLines(highlight)[0].scale.x

      highlight.update(0.4) // partway through the ~0.9s activation

      const scaleAfter = findShockwaveLines(highlight)[0].scale.x
      const ring = findBorderRing(highlight) as THREE.Mesh
      expect(scaleAfter).toBeGreaterThan(scaleBefore)
      expect((ring.material as THREE.ShaderMaterial).uniforms.uIntro.value).toBeGreaterThan(0)
    })

    it('removes the shockwave and settles the border ring to full intro once the activation window elapses', () => {
      const highlight = createAnimatedBorderHighlight(squareRing, 'high')

      highlight.update(2) // well past the ~0.9s activation window

      expect(findShockwaveLines(highlight)).toHaveLength(0)
      const ring = findBorderRing(highlight) as THREE.Mesh
      expect((ring.material as THREE.ShaderMaterial).uniforms.uIntro.value).toBe(1)
    })

    it('never builds a shockwave for low confidence', () => {
      const highlight = createAnimatedBorderHighlight(squareRing, 'low')
      highlight.update(0.1)
      expect(findShockwaveLines(highlight)).toHaveLength(0)
    })
  })
})
