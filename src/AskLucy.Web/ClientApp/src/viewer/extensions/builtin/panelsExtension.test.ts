import { renderHook } from '@testing-library/react'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useFloatingPanelStore } from '../../panels/store/floatingPanelStore'
import type { ExtensionContext } from '../context'
import type { ToolbarEntry } from '../ViewerExtension'
import { panelsExtension } from './panelsExtension'

const initialState = useFloatingPanelStore.getState()

function startAndCaptureEntry(): ToolbarEntry {
  const contributeToolbarEntry = vi.fn()
  const context = {
    engine: {},
    on: vi.fn(),
    contributeOverlay: vi.fn(),
    contributeHudItem: vi.fn(),
    contributeToolbarEntry,
    registerLivePanelKind: vi.fn(),
    openPanel: vi.fn(),
    acquireDrawingSpace: vi.fn(),
  } as unknown as ExtensionContext
  void panelsExtension.start(context)
  expect(contributeToolbarEntry).toHaveBeenCalledTimes(1)
  return contributeToolbarEntry.mock.calls[0][0] as ToolbarEntry
}

describe('panelsExtension "Arrange panels" toolbar entry (specs/054 FR-005e)', () => {
  beforeEach(() => {
    useFloatingPanelStore.setState(initialState, true)
  })

  it('runs the host-registered arrange handler when clicked', () => {
    const arrange = vi.fn()
    useFloatingPanelStore.getState().setArrangeHandler(arrange)

    startAndCaptureEntry().onClick()

    expect(arrange).toHaveBeenCalledTimes(1)
  })

  it('is a no-op, not a crash, when no host has registered a handler yet', () => {
    expect(() => startAndCaptureEntry().onClick()).not.toThrow()
  })

  it('is shown only while at least one panel is open', () => {
    const entry = startAndCaptureEntry()
    expect(entry.useIsShown).toBeDefined()

    const { result, rerender } = renderHook(() => entry.useIsShown!())
    expect(result.current).toBe(false)

    useFloatingPanelStore.setState({ panels: [{ id: 'p' } as never] })
    rerender()
    expect(result.current).toBe(true)
  })
})
