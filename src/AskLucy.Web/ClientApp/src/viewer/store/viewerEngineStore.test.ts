import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerEngineStore } from './viewerEngineStore'

const initialState = useViewerEngineStore.getState()

describe('viewerEngineStore', () => {
  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
  })

  it('starts on the placeholder content mode with no selection and no layers', () => {
    const state = useViewerEngineStore.getState()
    expect(state.contentMode).toBe('placeholder')
    expect(state.selection).toEqual({ selectedLayerId: null, selectedElementId: null })
    expect(state.layers).toEqual([])
  })

  it('defaults camera.mode to isometric and rotationEnabled to true when reduced-motion is not set (jsdom default)', () => {
    // setupTests.ts stubs window.matchMedia to always report matches: false.
    expect(useViewerEngineStore.getState().camera).toEqual({
      mode: 'isometric',
      rotationEnabled: true,
    })
  })

  it('setContentMode updates contentMode', () => {
    useViewerEngineStore.getState().setContentMode('map')
    expect(useViewerEngineStore.getState().contentMode).toBe('map')
  })

  it('setCamera merges into the existing camera state', () => {
    useViewerEngineStore.getState().setCamera({ mode: 'plan' })
    expect(useViewerEngineStore.getState().camera).toEqual({ mode: 'plan', rotationEnabled: true })

    useViewerEngineStore.getState().setCamera({ rotationEnabled: false })
    expect(useViewerEngineStore.getState().camera).toEqual({ mode: 'plan', rotationEnabled: false })
  })

  it('setSelection replaces the selection state', () => {
    useViewerEngineStore.getState().setSelection({ selectedLayerId: 'gis', selectedElementId: 'marker' })
    expect(useViewerEngineStore.getState().selection).toEqual({
      selectedLayerId: 'gis',
      selectedElementId: 'marker',
    })
  })

  it('setLayers replaces the layers array', () => {
    const layers = [{ id: 'gis-1', kind: 'gis' as const, visible: true, zIndex: 0, metadata: {} }]
    useViewerEngineStore.getState().setLayers(layers)
    expect(useViewerEngineStore.getState().layers).toEqual(layers)
  })
})

describe('viewerEngineStore reduced-motion default (T046a, FR-016/SC-008)', () => {
  beforeEach(() => {
    vi.resetModules()
  })

  it('defaults camera.rotationEnabled to false when the user prefers reduced motion at load time', async () => {
    vi.doMock('../../hooks/usePrefersReducedMotion', () => ({ prefersReducedMotion: () => true }))
    const { useViewerEngineStore: freshStore } = await import('./viewerEngineStore')
    expect(freshStore.getState().camera.rotationEnabled).toBe(false)
    vi.doUnmock('../../hooks/usePrefersReducedMotion')
  })

  it('defaults camera.rotationEnabled to true when the user does not prefer reduced motion', async () => {
    vi.doMock('../../hooks/usePrefersReducedMotion', () => ({ prefersReducedMotion: () => false }))
    const { useViewerEngineStore: freshStore } = await import('./viewerEngineStore')
    expect(freshStore.getState().camera.rotationEnabled).toBe(true)
    vi.doUnmock('../../hooks/usePrefersReducedMotion')
  })
})
