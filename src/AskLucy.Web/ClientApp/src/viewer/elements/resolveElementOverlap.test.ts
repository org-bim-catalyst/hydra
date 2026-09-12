import { describe, expect, it } from 'vitest'
import { resolveElementOverlap } from './resolveElementOverlap'

describe('resolveElementOverlap (FR-030, T046)', () => {
  it('returns null for no candidates', () => {
    expect(resolveElementOverlap([])).toBeNull()
  })

  it('returns the only candidate when there is exactly one', () => {
    const only = { layerId: 'l1', elementId: 'e1', drawingSpaceOrder: 0, distanceToCamera: 10 }
    expect(resolveElementOverlap([only])).toBe(only)
  })

  it('picks the candidate with the highest drawing-space order across different spaces', () => {
    const earlier = { layerId: 'l1', elementId: 'e1', drawingSpaceOrder: 0, distanceToCamera: 5 }
    const later = { layerId: 'l2', elementId: 'e2', drawingSpaceOrder: 1, distanceToCamera: 100 }
    expect(resolveElementOverlap([earlier, later])).toBe(later)
  })

  it('within the same drawing space, picks the element nearest the camera', () => {
    const far = { layerId: 'l1', elementId: 'far', drawingSpaceOrder: 0, distanceToCamera: 50 }
    const near = { layerId: 'l1', elementId: 'near', drawingSpaceOrder: 0, distanceToCamera: 5 }
    expect(resolveElementOverlap([far, near])).toBe(near)
  })

  it('resolves ties deterministically to the last candidate in input order', () => {
    const a = { layerId: 'l1', elementId: 'a', drawingSpaceOrder: 0, distanceToCamera: 10 }
    const b = { layerId: 'l1', elementId: 'b', drawingSpaceOrder: 0, distanceToCamera: 10 }
    expect(resolveElementOverlap([a, b])).toBe(b)
  })
})
