import { renderHook } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { viewerEngine } from '../../viewer/engine/viewerEngineInstance'
import { useViewerEngineStore } from '../../viewer/store/viewerEngineStore'
import { useMapStyleControl, useViewModeControl } from './workspaceControls'

function wrapper({ children }: { children: ReactNode }) {
  return <MemoryRouter>{children}</MemoryRouter>
}

describe('useViewModeControl (specs/027-immersive-viewer-platform FR-013, research.md Decision 4)', () => {
  beforeEach(() => {
    useWorkspaceOverlayStore.setState({
      expandedControlId: null,
      viewMode: 'isometric',
      unreadControlIds: new Set(),
    })
  })

  it('exposes isometric and plan actions, highlighting the current mode', () => {
    const { result } = renderHook(() => useViewModeControl(), { wrapper })
    // ExpandableActionGroup receives the actions as props on its element — inspect via the
    // rendered control definition's own content element props rather than a full DOM render,
    // keeping this test focused on the control's data, not ExpandableActionGroup's rendering.
    const content = result.current.content as React.ReactElement<{
      actions: { id: string; label: string; highlighted?: boolean }[]
    }>
    const actions = content.props.actions
    expect(actions.map((a) => a.label)).toEqual(['Isometric', 'Plan'])
    expect(actions.find((a) => a.id === 'isometric')?.highlighted).toBe(true)
    expect(actions.find((a) => a.id === 'plan')?.highlighted).toBe(false)
  })

  it('selecting a mode updates workspaceOverlayStore and calls viewerEngine.setViewMode', () => {
    const setViewModeSpy = vi.spyOn(viewerEngine, 'setViewMode')
    const { result } = renderHook(() => useViewModeControl(), { wrapper })
    const content = result.current.content as React.ReactElement<{
      actions: { id: string; onSelect?: () => void }[]
    }>

    content.props.actions.find((a) => a.id === 'plan')?.onSelect?.()

    expect(useWorkspaceOverlayStore.getState().viewMode).toBe('plan')
    expect(setViewModeSpy).toHaveBeenCalledWith('plan')

    setViewModeSpy.mockRestore()
  })
})

describe('useMapStyleControl', () => {
  const initialViewerEngineState = useViewerEngineStore.getState()

  beforeEach(() => {
    useViewerEngineStore.setState(initialViewerEngineState, true)
    // specs/048-buildings-only-map-style FR-006: unset (raster rendering) unless a test opts
    // into the vector-rendering case below.
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', '')
  })

  afterEach(() => {
    vi.unstubAllEnvs()
  })

  it('exposes roadmap/satellite/hybrid/buildings-only actions, highlighting the current style', () => {
    const { result } = renderHook(() => useMapStyleControl(), { wrapper })
    const content = result.current.content as React.ReactElement<{
      actions: { id: string; label: string; highlighted?: boolean }[]
    }>
    const actions = content.props.actions
    expect(actions.map((a) => a.label)).toEqual(['Road map', 'Satellite', 'Hybrid', 'Buildings only'])
    expect(actions.find((a) => a.id === 'roadmap')?.highlighted).toBe(true)
    expect(actions.find((a) => a.id === 'satellite')?.highlighted).toBe(false)
    expect(actions.find((a) => a.id === 'hybrid')?.highlighted).toBe(false)
    expect(actions.find((a) => a.id === 'buildings-only')?.highlighted).toBe(false)
  })

  it('selecting a style calls viewerEngine.setMapStyle, which updates viewerEngineStore', () => {
    const setMapStyleSpy = vi.spyOn(viewerEngine, 'setMapStyle')
    const { result } = renderHook(() => useMapStyleControl(), { wrapper })
    const content = result.current.content as React.ReactElement<{
      actions: { id: string; onSelect?: () => void }[]
    }>

    content.props.actions.find((a) => a.id === 'satellite')?.onSelect?.()

    expect(setMapStyleSpy).toHaveBeenCalledWith('satellite')
    expect(useViewerEngineStore.getState().mapStyle).toBe('satellite')

    setMapStyleSpy.mockRestore()
  })

  it('selecting "Buildings only" calls viewerEngine.setMapStyle and highlights it as active (US1)', () => {
    const setMapStyleSpy = vi.spyOn(viewerEngine, 'setMapStyle')
    const { result, rerender } = renderHook(() => useMapStyleControl(), { wrapper })
    const content = result.current.content as React.ReactElement<{
      actions: { id: string; onSelect?: () => void }[]
    }>

    content.props.actions.find((a) => a.id === 'buildings-only')?.onSelect?.()
    rerender()

    expect(setMapStyleSpy).toHaveBeenCalledWith('buildings-only')
    expect(useViewerEngineStore.getState().mapStyle).toBe('buildings-only')
    const updatedContent = result.current.content as React.ReactElement<{
      actions: { id: string; highlighted?: boolean }[]
    }>
    expect(updatedContent.props.actions.find((a) => a.id === 'buildings-only')?.highlighted).toBe(true)

    setMapStyleSpy.mockRestore()
  })

  it('omits "Buildings only" when a Map ID is configured with no buildings-only Map ID (vector rendering, no alternate style, FR-006/US3)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', 'a-real-vector-map-id')
    const { result } = renderHook(() => useMapStyleControl(), { wrapper })
    const content = result.current.content as React.ReactElement<{
      actions: { id: string }[]
    }>

    expect(content.props.actions.map((a) => a.id)).toEqual(['roadmap', 'satellite', 'hybrid'])
  })

  it('offers "Buildings only" when both a Map ID and a buildings-only Map ID are configured (vector rendering, research.md Decision 4)', () => {
    vi.stubEnv('VITE_GOOGLE_MAPS_MAP_ID', 'a-real-vector-map-id')
    vi.stubEnv('VITE_GOOGLE_MAPS_BUILDINGS_ONLY_MAP_ID', 'a-buildings-only-map-id')
    const { result } = renderHook(() => useMapStyleControl(), { wrapper })
    const content = result.current.content as React.ReactElement<{
      actions: { id: string }[]
    }>

    expect(content.props.actions.map((a) => a.id)).toEqual(['roadmap', 'satellite', 'hybrid', 'buildings-only'])
  })
})
