import { describe, expect, it } from 'vitest'
import type { GeoPoint } from '../../store/activeSiteBoundaryStore'
import type { CameraViewMode } from '../api/commands'
import {
  FRAME_PADDING_PX,
  captureViewState,
  enterPlanForEditing,
  restoreViewState,
  type EditableViewEngine,
  type EditableViewMap,
  type ViewStateDeps,
} from './viewStateCapture'

/** A fake map that behaves like Google's: moveCamera writes only the fields it is given. */
function fakeWorld(initial: { mode: CameraViewMode; rotationEnabled: boolean; zoom: number; heading: number; tilt: number; lat: number; lng: number }) {
  const calls: string[] = []
  const state = { ...initial }

  const map: EditableViewMap = {
    getCenter: () => ({ lat: () => state.lat, lng: () => state.lng }),
    getZoom: () => state.zoom,
    getHeading: () => state.heading,
    getTilt: () => state.tilt,
    moveCamera: (options) => {
      calls.push('moveCamera')
      if (options.center) {
        state.lat = options.center.lat
        state.lng = options.center.lng
      }
      if (options.zoom !== undefined) state.zoom = options.zoom
      if (options.heading !== undefined) state.heading = options.heading
      if (options.tilt !== undefined) state.tilt = options.tilt
    },
    fitBounds: (bounds, padding) => {
      calls.push(`fitBounds:${padding}`)
      state.lat = (bounds.north + bounds.south) / 2
      state.lng = (bounds.east + bounds.west) / 2
      state.zoom = 19
    },
  }

  const engine: EditableViewEngine = {
    setViewMode: (mode) => {
      calls.push(`mode:${mode}`)
      state.mode = mode
    },
    setRotationEnabled: (enabled) => {
      calls.push(`rotation:${enabled}`)
      state.rotationEnabled = enabled
    },
  }

  const deps: ViewStateDeps = { map, engine, camera: () => ({ mode: state.mode, rotationEnabled: state.rotationEnabled }) }
  return { deps, state, calls }
}

const RINGS: GeoPoint[][] = [
  [
    { latitude: 23.586, longitude: 58.392 },
    { latitude: 23.587, longitude: 58.394 },
    { latitude: 23.585, longitude: 58.395 },
  ],
  [
    { latitude: 23.588, longitude: 58.396 },
    { latitude: 23.589, longitude: 58.397 },
    { latitude: 23.5875, longitude: 58.3975 },
  ],
]

describe('captureViewState', () => {
  it('reads the mode, rotation flag, centre, zoom, heading and tilt', () => {
    const { deps } = fakeWorld({ mode: 'isometric', rotationEnabled: true, zoom: 17.4, heading: 42, tilt: 45, lat: 23.59, lng: 58.4 })

    expect(captureViewState(deps)).toEqual({
      mode: 'isometric',
      rotationEnabled: true,
      center: { latitude: 23.59, longitude: 58.4 },
      zoom: 17.4,
      heading: 42,
      tilt: 45,
    })
  })
})

describe('enterPlanForEditing', () => {
  it('stops rotation first, then goes to plan, faces north and frames every ring', () => {
    const { deps, state, calls } = fakeWorld({ mode: 'isometric', rotationEnabled: true, zoom: 17, heading: 42, tilt: 45, lat: 23.5, lng: 58.3 })

    enterPlanForEditing(deps, RINGS)

    expect(calls).toEqual(['rotation:false', 'mode:plan', 'moveCamera', `fitBounds:${FRAME_PADDING_PX}`])
    expect(state).toMatchObject({ mode: 'plan', rotationEnabled: false, heading: 0, tilt: 0 })
    // Framed on the middle of both rings together.
    expect(state.lat).toBeCloseTo((23.589 + 23.585) / 2, 6)
    expect(state.lng).toBeCloseTo((58.3975 + 58.392) / 2, 6)
  })

  it('does not frame anything when there are no corners', () => {
    const { deps, calls } = fakeWorld({ mode: 'plan', rotationEnabled: false, zoom: 17, heading: 0, tilt: 0, lat: 0, lng: 0 })

    enterPlanForEditing(deps, [])

    expect(calls.some((c) => c.startsWith('fitBounds'))).toBe(false)
  })
})

describe('restoreViewState', () => {
  it('restores the mode, then the exact camera, and rotation last', () => {
    const { deps, calls } = fakeWorld({ mode: 'isometric', rotationEnabled: true, zoom: 17, heading: 42, tilt: 45, lat: 23.59, lng: 58.4 })
    const before = captureViewState(deps)

    enterPlanForEditing(deps, RINGS)
    calls.length = 0
    restoreViewState(deps, before)

    expect(calls).toEqual(['mode:isometric', 'moveCamera', 'rotation:true'])
  })

  // SC-002: the view matches in every checked respect for all four combinations.
  it.each([
    ['isometric', true],
    ['isometric', false],
    ['plan', true],
    ['plan', false],
  ] as const)('puts the view back exactly (%s, rotating=%s)', (mode, rotationEnabled) => {
    const { deps, state } = fakeWorld({
      mode,
      rotationEnabled,
      zoom: 16.75,
      heading: 137,
      tilt: mode === 'plan' ? 0 : 45,
      lat: 23.5912,
      lng: 58.4021,
    })
    const before = captureViewState(deps)

    enterPlanForEditing(deps, RINGS)
    expect(state.rotationEnabled).toBe(false)
    expect(state.mode).toBe('plan')

    restoreViewState(deps, before)

    expect(captureViewState(deps)).toEqual(before)
    expect(state.rotationEnabled).toBe(rotationEnabled)
  })

  it('keepCamera puts the mode and rotation back but leaves the camera where it is (a different site is being shown)', () => {
    const { deps, calls, state } = fakeWorld({ mode: 'isometric', rotationEnabled: true, zoom: 17, heading: 42, tilt: 45, lat: 23.59, lng: 58.4 })
    const before = captureViewState(deps)
    enterPlanForEditing(deps, RINGS)
    state.lat = 25.25
    state.lng = 55.3
    calls.length = 0

    restoreViewState(deps, before, { keepCamera: true })

    expect(calls).toEqual(['mode:isometric', 'rotation:true'])
    expect(state.lat).toBe(25.25)
    expect(state.lng).toBe(55.3)
  })

  it('does not start rotation when it was not rotating', () => {
    const { deps, state } = fakeWorld({ mode: 'isometric', rotationEnabled: false, zoom: 17, heading: 10, tilt: 45, lat: 1, lng: 2 })
    const before = captureViewState(deps)

    enterPlanForEditing(deps, RINGS)
    restoreViewState(deps, before)

    expect(state.rotationEnabled).toBe(false)
  })
})
