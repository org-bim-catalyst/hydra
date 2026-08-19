import { describe, expect, it } from 'vitest'
import { resolveSelection } from './resolveSelection'

describe('resolveSelection (spec.md Edge Cases — overlapping selectable content)', () => {
  it('returns null for no candidates', () => {
    expect(resolveSelection([])).toBeNull()
  })

  it('returns the only candidate when there is exactly one', () => {
    const candidate = { layerId: 'gis-1', elementId: 'marker', zIndex: 0 }
    expect(resolveSelection([candidate])).toBe(candidate)
  })

  it('picks the candidate with the highest zIndex when several overlap', () => {
    const low = { layerId: 'gis-1', elementId: 'marker', zIndex: 0 }
    const high = { layerId: 'overlay-1', elementId: 'shape', zIndex: 5 }
    const mid = { layerId: 'model-1', elementId: 'part', zIndex: 2 }

    expect(resolveSelection([low, high, mid])).toBe(high)
  })

  it('resolves ties deterministically to the last candidate in input order', () => {
    const first = { layerId: 'a', elementId: 'x', zIndex: 3 }
    const second = { layerId: 'b', elementId: 'y', zIndex: 3 }

    expect(resolveSelection([first, second])).toBe(second)
  })
})
