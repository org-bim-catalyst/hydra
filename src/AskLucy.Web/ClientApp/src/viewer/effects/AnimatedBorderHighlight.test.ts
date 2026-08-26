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

  it('renders two animated comets for high confidence (FR-006 visual distinction)', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    // 1 static perimeter line + 2 comet lines
    expect(highlight.object3D.children.filter((c) => c instanceof THREE.Line)).toHaveLength(3)
  })

  it('renders one, slower comet for medium confidence', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'medium')
    expect(highlight.object3D.children.filter((c) => c instanceof THREE.Line)).toHaveLength(2)
  })

  it('renders no comets for low confidence — static, muted perimeter only', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'low')
    expect(highlight.object3D.children.filter((c) => c instanceof THREE.Line)).toHaveLength(1)
  })

  it('setConfidenceLevel rebuilds the comet set without needing a new instance', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    highlight.setConfidenceLevel('low')
    expect(highlight.object3D.children.filter((c) => c instanceof THREE.Line)).toHaveLength(1)

    highlight.setConfidenceLevel('high')
    expect(highlight.object3D.children.filter((c) => c instanceof THREE.Line)).toHaveLength(3)
  })

  it('update() advances the comet position, changing its geometry each frame', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    const cometLine = highlight.object3D.children.find(
      (c) => c instanceof THREE.Line && c.material instanceof THREE.ShaderMaterial,
    ) as THREE.Line
    const positionsBefore = (cometLine.geometry.getAttribute('position') as THREE.BufferAttribute).array.slice()

    highlight.update(0.5)

    const positionsAfter = (cometLine.geometry.getAttribute('position') as THREE.BufferAttribute).array
    expect(Array.from(positionsAfter)).not.toEqual(Array.from(positionsBefore))
  })

  it('dispose() removes all children and does not throw', () => {
    const highlight = createAnimatedBorderHighlight(squareRing, 'high')
    expect(() => highlight.dispose()).not.toThrow()
    expect(highlight.object3D.children).toHaveLength(0)
  })
})
