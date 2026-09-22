import { describe, expect, it, vi } from 'vitest'
import { CameraRestoreGuard } from './cameraRestoreGuard'

function makeTarget(initial: { zoom: number; heading: number }) {
  const state = { ...initial }
  return {
    state,
    getCamera: () => ({ ...state }),
    setCamera: vi.fn((camera: { zoom?: number; heading?: number }) => {
      if (camera.zoom !== undefined) state.zoom = camera.zoom
      if (camera.heading !== undefined) state.heading = camera.heading
    }),
  }
}

describe('CameraRestoreGuard', () => {
  const enforceHeading = { shouldEnforceHeading: () => true }
  const rotationOwnsHeading = { shouldEnforceHeading: () => false }

  it('re-applies a zoom that Maps JS snapped to a whole level after construction', () => {
    // The exact failure this exists for: the map is constructed at 17.526 and Google's own
    // post-construction initialisation rounds it to 18.
    const target = makeTarget({ zoom: 18, heading: 44.0832 })
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    guard.enforce()

    expect(target.setCamera).toHaveBeenCalledWith({ zoom: 17.526 })
    expect(target.state.zoom).toBe(17.526)
  })

  it('re-applies a heading that Maps JS zeroed after construction', () => {
    const target = makeTarget({ zoom: 17.526, heading: 0 })
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    guard.enforce()

    expect(target.setCamera).toHaveBeenCalledWith({ heading: 44.0832 })
  })

  it('corrects zoom and heading in a single atomic write', () => {
    const target = makeTarget({ zoom: 18, heading: 0 })
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    guard.enforce()

    expect(target.setCamera).toHaveBeenCalledTimes(1)
    expect(target.setCamera).toHaveBeenCalledWith({ zoom: 17.526, heading: 44.0832 })
  })

  it('leaves heading alone while auto-rotation owns it', () => {
    // The rotation driver is seeded with the restored heading and resumes from it, so a second
    // writer here would fight it every frame.
    const target = makeTarget({ zoom: 18, heading: 0 })
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, rotationOwnsHeading)

    guard.enforce()

    expect(target.setCamera).toHaveBeenCalledWith({ zoom: 17.526 })
  })

  it('releases once the map matches, so it never fights a later camera change', () => {
    const target = makeTarget({ zoom: 18, heading: 0 })
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    guard.enforce()
    expect(guard.isActive).toBe(true)

    guard.enforce()
    expect(guard.isActive).toBe(false)

    // A camera change after the guard released — a user zooming — must not be undone.
    target.state.zoom = 12
    guard.enforce()
    expect(target.setCamera).toHaveBeenCalledTimes(1)
  })

  it('stops enforcing after the user takes hold of the camera', () => {
    const target = makeTarget({ zoom: 18, heading: 0 })
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    guard.release()
    guard.enforce()

    expect(target.setCamera).not.toHaveBeenCalled()
    expect(guard.isActive).toBe(false)
  })

  it('gives up rather than looping forever against a map that keeps overwriting the camera', () => {
    const state = { zoom: 18, heading: 0 }
    const target = {
      getCamera: () => ({ ...state }),
      // Accepts the write and immediately discards it, the pathological case this bound exists
      // for — without it, every settle would produce another correction indefinitely.
      setCamera: vi.fn(() => {}),
    }
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    for (let i = 0; i < 50; i += 1) guard.enforce()

    expect(target.setCamera.mock.calls.length).toBeLessThanOrEqual(20)
    expect(guard.isActive).toBe(false)
  })

  it('does nothing while the map has no readable camera yet', () => {
    const target = { getCamera: () => null, setCamera: vi.fn() }
    const guard = new CameraRestoreGuard(target, { zoom: 17.526, heading: 44.0832 }, enforceHeading)

    guard.enforce()

    expect(target.setCamera).not.toHaveBeenCalled()
    expect(guard.isActive).toBe(true)
  })
})
