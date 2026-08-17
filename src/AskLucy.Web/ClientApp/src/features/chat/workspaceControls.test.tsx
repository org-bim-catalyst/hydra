import { renderHook } from '@testing-library/react'
import { MemoryRouter } from 'react-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import type { ReactNode } from 'react'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'
import { viewerEngine } from '../../viewer/engine/viewerEngineInstance'
import { useViewModeControl } from './workspaceControls'

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
