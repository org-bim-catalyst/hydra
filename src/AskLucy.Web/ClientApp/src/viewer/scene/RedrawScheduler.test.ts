import { beforeEach, describe, expect, it, vi } from 'vitest'
import { RedrawScheduler, redrawScheduler } from './RedrawScheduler'

describe('RedrawScheduler (FR-020, FR-021, FR-022, FR-024, research D4)', () => {
  beforeEach(() => {
    // Reset the singleton's bound callback and pending state between tests.
    redrawScheduler.bind(() => {})
    redrawScheduler.frameRendered()
  })

  it('coalesces N calls before the next frame into exactly one downstream redraw', () => {
    const requestRedraw = vi.fn()
    redrawScheduler.bind(requestRedraw)

    redrawScheduler.invalidate()
    redrawScheduler.invalidate()
    redrawScheduler.invalidate()

    expect(requestRedraw).toHaveBeenCalledTimes(1)
  })

  it('produces zero redraws when invalidate() is never called (no idle redraw, FR-022)', () => {
    const requestRedraw = vi.fn()
    redrawScheduler.bind(requestRedraw)

    expect(requestRedraw).not.toHaveBeenCalled()
  })

  it('schedules a fresh redraw for the next frame after frameRendered() clears the pending window', () => {
    const requestRedraw = vi.fn()
    redrawScheduler.bind(requestRedraw)

    redrawScheduler.invalidate()
    redrawScheduler.frameRendered()
    redrawScheduler.invalidate()

    expect(requestRedraw).toHaveBeenCalledTimes(2)
  })

  it('is a safe no-op when nothing is bound yet', () => {
    const unbound = new RedrawScheduler()
    expect(() => unbound.invalidate()).not.toThrow()
  })

  it('is a safe no-op for a caller with no live handle to call it from (FR-024)', () => {
    // There is no per-caller tracking to "reject" — a stopped extension simply holds no
    // DrawingSpaceHandle to call invalidate() through in the first place (specs/050's
    // contribution-teardown guarantee is what makes this true, not anything RedrawScheduler
    // itself checks). This asserts the scheduler's own side is unconditionally safe regardless.
    const requestRedraw = vi.fn()
    redrawScheduler.bind(requestRedraw)
    redrawScheduler.invalidate()
    redrawScheduler.frameRendered()

    expect(() => redrawScheduler.invalidate()).not.toThrow()
  })
})
