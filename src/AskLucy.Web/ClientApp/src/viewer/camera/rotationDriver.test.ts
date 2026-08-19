import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { RotationDriver } from './rotationDriver'

describe('RotationDriver (FR-014/FR-015, US3-AC3/AC4)', () => {
  let pendingFrames: Map<number, FrameRequestCallback>
  let nextFrameId: number

  beforeEach(() => {
    pendingFrames = new Map()
    nextFrameId = 1
    vi.stubGlobal('requestAnimationFrame', (cb: FrameRequestCallback) => {
      const id = nextFrameId++
      pendingFrames.set(id, cb)
      return id
    })
    vi.stubGlobal('cancelAnimationFrame', (id: number) => {
      pendingFrames.delete(id)
    })
  })

  afterEach(() => {
    vi.unstubAllGlobals()
  })

  /** Flushes exactly the single oldest still-pending frame — mirrors a real browser, where a
   * cancelled frame is removed from the queue and never fires. */
  function flushOneFrame(timestamp: number) {
    const oldestId = Math.min(...pendingFrames.keys())
    const cb = pendingFrames.get(oldestId)
    pendingFrames.delete(oldestId)
    cb?.(timestamp)
  }

  it('does not call setHeading until enabled', () => {
    const setHeading = vi.fn()
    const driver = new RotationDriver({ setHeading })
    expect(pendingFrames.size).toBe(0)
    expect(setHeading).not.toHaveBeenCalled()
    void driver
  })

  it('advances heading over time once enabled, and holds it when disabled (FR-015)', () => {
    const setHeading = vi.fn()
    const driver = new RotationDriver({ setHeading })

    driver.setEnabled(true)
    flushOneFrame(0) // first frame only establishes lastTimestamp, no movement yet
    flushOneFrame(1000) // 1 second later → 6 degrees per second

    expect(setHeading).toHaveBeenCalledWith(6)

    driver.setEnabled(false)
    expect(pendingFrames.size).toBe(0) // the pending next-frame request was genuinely cancelled
  })

  it('resumes from the same heading rather than resetting to 0 (US3-AC4)', () => {
    const setHeading = vi.fn()
    const driver = new RotationDriver({ setHeading })

    driver.setEnabled(true)
    flushOneFrame(0)
    flushOneFrame(1000) // heading = 6
    driver.setEnabled(false)

    driver.setEnabled(true)
    flushOneFrame(2000) // re-establishes lastTimestamp at 2000, no movement yet
    flushOneFrame(3000) // 1 more second → 6 + 6 = 12, not reset to 6

    expect(setHeading).toHaveBeenLastCalledWith(12)
  })

  it('dispose cancels any in-flight frame', () => {
    const driver = new RotationDriver({ setHeading: vi.fn() })
    driver.setEnabled(true)
    expect(pendingFrames.size).toBe(1)
    driver.dispose()
    expect(pendingFrames.size).toBe(0)
  })
})
