import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { rememberCamera, restoreRememberedCamera, setCameraMemoryChat } from './chatCameraMemory'

const CAMERA = { locationKey: '23.5900,58.4000', latitude: 23.59, longitude: 58.4, zoom: 17.5, heading: 30, tilt: 45 }

interface FakeMap {
  handlers: Record<string, () => void>
  moveCamera: ReturnType<typeof vi.fn>
  getDiv(): HTMLElement
  addListener: ReturnType<typeof vi.fn>
}

const created: FakeMap[] = []

function fakeMap(): FakeMap {
  const handlers: Record<string, () => void> = {}
  const map = {
    handlers,
    moveCamera: vi.fn(),
    getDiv: () => document.createElement('div'),
    addListener: vi.fn((event: string, handler: () => void) => {
      handlers[event] = handler
      return {
        remove: vi.fn(() => {
          delete handlers[event]
        }),
      }
    }),
  }
  created.push(map)
  return map
}

const KEY = '23.5900,58.4000'
let counter = 0
const freshChat = () => `chat-${++counter}-${Math.random()}`

beforeEach(() => {
  localStorage.clear()
  useGoogleMapsStore.setState({ map: null, handle: null })
})

afterEach(() => {
  // A restore left running by one test must not bleed into the next: the user taking over ends it.
  created.splice(0).forEach((m) => m.handlers.dragstart?.())
  setCameraMemoryChat(null)
  useGoogleMapsStore.setState({ map: null, handle: null })
})

describe('chatCameraMemory', () => {
  it('saves the camera under the current chat and puts it back after a reload', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)
    expect(JSON.parse(localStorage.getItem(`asklucy.camera.${chatId}`)!)).toEqual(CAMERA)

    const map = fakeMap()
    useGoogleMapsStore.setState({ map: map as never })
    restoreRememberedCamera(chatId, KEY)

    expect(map.moveCamera).toHaveBeenCalledWith({ center: { lat: 23.59, lng: 58.4 }, zoom: 17.5, heading: 30, tilt: 45 })
  })

  it('saves nothing before a chat is known', () => {
    rememberCamera(CAMERA)
    expect(localStorage.length).toBe(0)
  })

  it('does not overwrite the saved camera while it is being put back, and saves again once the user takes the map over', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)
    const map = fakeMap()
    useGoogleMapsStore.setState({ map: map as never })
    restoreRememberedCamera(chatId, KEY)

    rememberCamera({ ...CAMERA, zoom: 12 })
    expect(JSON.parse(localStorage.getItem(`asklucy.camera.${chatId}`)!).zoom).toBe(17.5)

    map.handlers.dragstart()
    rememberCamera({ ...CAMERA, zoom: 12 })
    expect(JSON.parse(localStorage.getItem(`asklucy.camera.${chatId}`)!).zoom).toBe(12)
  })

  it('stops restoring as soon as the user drags the map', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)
    const map = fakeMap()
    useGoogleMapsStore.setState({ map: map as never })
    restoreRememberedCamera(chatId, KEY)

    map.handlers.dragstart()

    expect(map.handlers.idle).toBeUndefined()

    expect(map.moveCamera).toHaveBeenCalledTimes(1)
  })

  it('does nothing for a chat with nothing saved, and ignores a damaged entry', () => {
    const none = freshChat()
    const map = fakeMap()
    useGoogleMapsStore.setState({ map: map as never })
    restoreRememberedCamera(none, KEY)

    const bad = freshChat()
    localStorage.setItem(`asklucy.camera.${bad}`, '{"zoom":"x"}')
    restoreRememberedCamera(bad, KEY)

    expect(map.moveCamera).not.toHaveBeenCalled()
  })

  it('does not put a camera back over a different place', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)
    const map = fakeMap()
    useGoogleMapsStore.setState({ map: map as never })

    restoreRememberedCamera(chatId, '25.1972,55.2744')

    expect(map.moveCamera).not.toHaveBeenCalled()
  })

  it('restores a chat once per page load', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)
    const map = fakeMap()
    useGoogleMapsStore.setState({ map: map as never })

    restoreRememberedCamera(chatId, KEY)
    restoreRememberedCamera(chatId, KEY)

    expect(map.moveCamera).toHaveBeenCalledTimes(1)
  })
})
