import { act, renderHook } from '@testing-library/react'
import { describe, expect, it, vi } from 'vitest'
import { useActionInvoker } from './useActionInvoker'

const clearSelectionMock = vi.fn()
const setLayerVisibilityMock = vi.fn()

vi.mock('../../engine/viewerEngineInstance', () => ({
  viewerEngine: {
    clearSelection: () => clearSelectionMock(),
    setLayerVisibility: (layerId: string, visible: boolean) => setLayerVisibilityMock(layerId, visible),
  },
}))

describe('useActionInvoker (spec FR-015)', () => {
  it('invokes a valid action through the viewer engine and reports no error', () => {
    clearSelectionMock.mockReturnValue({ ok: true })
    const { result } = renderHook(() => useActionInvoker())

    act(() => result.current.invoke({ command: 'clearSelection', args: {} }))

    expect(clearSelectionMock).toHaveBeenCalled()
    expect(result.current.error).toBeNull()
  })

  it('surfaces the engine failure message when a command reports it could not take effect', () => {
    setLayerVisibilityMock.mockReturnValue({ ok: false, error: 'No layer with id "gone" is registered.' })
    const { result } = renderHook(() => useActionInvoker())

    act(() => result.current.invoke({ command: 'setLayerVisibility', args: { layerId: 'gone', visible: false } }))

    expect(result.current.error).toBe('No layer with id "gone" is registered.')
  })

  it('never invokes a command that is not on the allowlist', () => {
    const { result } = renderHook(() => useActionInvoker())

    act(() => result.current.invoke({ command: 'removeLayer', args: { layerId: 'x' } }))

    expect(result.current.error).toContain('not a permitted action')
  })
})
