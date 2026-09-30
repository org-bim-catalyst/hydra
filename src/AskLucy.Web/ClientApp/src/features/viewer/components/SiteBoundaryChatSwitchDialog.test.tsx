import { act, cleanup, fireEvent, render, screen } from '@testing-library/react'
import { afterEach, beforeEach, describe, expect, it, vi, type Mock } from 'vitest'
import type { GeoPoint } from '../../../store/activeSiteBoundaryStore'
import { guardChatSwitch, useChatSwitchGuardStore } from '../../../viewer/siteBoundaryEdit/chatSwitchGuard'
import { registerSiteBoundaryEditRuntime, type SiteBoundaryEditRuntime } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore, type ViewState } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryChatSwitchDialog } from './SiteBoundaryChatSwitchDialog'

const P = (lat: number, lon: number): GeoPoint => ({ latitude: lat, longitude: lon })
const SQUARE = [P(23.586, 58.392), P(23.586, 58.393), P(23.587, 58.393), P(23.587, 58.392)]
const viewState: ViewState = { mode: 'isometric', rotationEnabled: true, center: P(23.586, 58.392), zoom: 17, heading: 0, tilt: 45 }
const store = () => useSiteBoundaryEditStore.getState()

let runtime: { done: ReturnType<typeof vi.fn>; cancel: ReturnType<typeof vi.fn> }
let unregister: (() => void) | null
let proceed: Mock<() => void>

const dirtySession = () => {
  store().enter({ chatId: 'chat-1', siteName: 'S', revision: 'r', rings: [SQUARE], viewState })
  store().applyChange({ op: 'move', ring: 0, index: 1, before: SQUARE[1], after: P(23.586, 58.3931) })
}

beforeEach(() => {
  proceed = vi.fn<() => void>()
  runtime = { done: vi.fn().mockResolvedValue(undefined), cancel: vi.fn() }
  unregister = registerSiteBoundaryEditRuntime(runtime as unknown as SiteBoundaryEditRuntime)
})

afterEach(() => {
  cleanup()
  unregister?.()
  useChatSwitchGuardStore.getState().release()
  store().end()
})

describe('guardChatSwitch', () => {
  it('goes straight ahead when there is no edit session', () => {
    guardChatSwitch('chat-2', proceed)
    expect(proceed).toHaveBeenCalledTimes(1)
  })

  it('goes straight ahead when nothing has changed', () => {
    store().enter({ chatId: 'chat-1', siteName: 'S', revision: 'r', rings: [SQUARE], viewState })
    guardChatSwitch('chat-2', proceed)
    expect(proceed).toHaveBeenCalledTimes(1)
  })

  it('goes straight ahead when the target is the chat being edited', () => {
    dirtySession()
    guardChatSwitch('chat-1', proceed)
    expect(proceed).toHaveBeenCalledTimes(1)
  })

  it('holds the switch when there are unsaved changes, for another chat or a new one', () => {
    dirtySession()
    guardChatSwitch('chat-2', proceed)
    guardChatSwitch(null, proceed)
    expect(proceed).not.toHaveBeenCalled()
    expect(useChatSwitchGuardStore.getState().pending).not.toBeNull()
  })
})

describe('SiteBoundaryChatSwitchDialog', () => {
  it('Stay cancels the switch and keeps the edit', () => {
    dirtySession()
    render(<SiteBoundaryChatSwitchDialog />)
    act(() => guardChatSwitch('chat-2', proceed))

    fireEvent.click(screen.getByText('Stay'))

    expect(proceed).not.toHaveBeenCalled()
    expect(store().session).not.toBeNull()
  })

  it('Discard drops the edit and then switches', () => {
    dirtySession()
    render(<SiteBoundaryChatSwitchDialog />)
    act(() => guardChatSwitch('chat-2', proceed))

    fireEvent.click(screen.getByText('Discard'))

    expect(runtime.cancel).toHaveBeenCalledTimes(1)
    expect(proceed).toHaveBeenCalledTimes(1)
  })

  it('Save saves first and switches only once the session has ended', async () => {
    dirtySession()
    runtime.done.mockImplementation(async () => store().end())
    render(<SiteBoundaryChatSwitchDialog />)
    act(() => guardChatSwitch('chat-2', proceed))

    await act(async () => {
      fireEvent.click(screen.getByText('Save'))
    })

    expect(runtime.done).toHaveBeenCalledTimes(1)
    expect(proceed).toHaveBeenCalledTimes(1)
  })

  it('does not switch, and says why, when the save failed', async () => {
    dirtySession()
    render(<SiteBoundaryChatSwitchDialog />)
    act(() => guardChatSwitch('chat-2', proceed))

    await act(async () => {
      fireEvent.click(screen.getByText('Save'))
    })

    expect(proceed).not.toHaveBeenCalled()
    expect(screen.getByText(/could not be saved/)).toBeInTheDocument()
  })

  it('on a page with no map, Discard ends the session itself and Save becomes a way back', () => {
    dirtySession()
    unregister?.()
    unregister = null
    const back = vi.fn()
    render(<SiteBoundaryChatSwitchDialog onReturnToEditor={back} />)
    act(() => guardChatSwitch('chat-2', proceed))

    fireEvent.click(screen.getByText('Go back and save'))
    expect(back).toHaveBeenCalledTimes(1)
    expect(proceed).not.toHaveBeenCalled()

    act(() => guardChatSwitch('chat-2', proceed))
    fireEvent.click(screen.getByText('Discard'))
    expect(store().session).toBeNull()
    expect(proceed).toHaveBeenCalledTimes(1)
  })
})
