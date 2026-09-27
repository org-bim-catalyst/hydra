import { describe, expect, it } from 'vitest'
import { createSiteBoundaryRenderer } from './SiteBoundaryRenderer'
import type { LocalPoint } from '../../effects/AnimatedBorderHighlight'

const square = (x: number, y: number, size: number): LocalPoint[] => [
  { x, y },
  { x: x + size, y },
  { x: x + size, y: y + size },
  { x, y: y + size },
]

describe('createSiteBoundaryRenderer', () => {
  it('gives the site and each of its separate buildings their own animated border (specs/077)', () => {
    const renderer = createSiteBoundaryRenderer()

    renderer.setRings([square(0, 0, 100), square(150, 150, 60)], 'high')

    expect(renderer.object3D.children).toHaveLength(2)
    expect(() => renderer.update(0.016, 0.5)).not.toThrow()
  })

  it('replaces the rings on the next call and clears them on null', () => {
    const renderer = createSiteBoundaryRenderer()
    renderer.setRings([square(0, 0, 100), square(150, 150, 60)], 'high')

    renderer.setRings([square(0, 0, 100)], 'medium')
    expect(renderer.object3D.children).toHaveLength(1)

    renderer.setRings(null, 'low')
    expect(renderer.object3D.children).toHaveLength(0)
  })

  it('skips a ring too short to be an outline', () => {
    const renderer = createSiteBoundaryRenderer()

    renderer.setRings([square(0, 0, 100), [{ x: 0, y: 0 }, { x: 1, y: 1 }]], 'high')

    expect(renderer.object3D.children).toHaveLength(1)
  })
})
