import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { useGoogleMapsStore } from '../store/googleMapsStore'
import { rememberCamera, restoreRememberedCamera, setCameraMemoryChat } from './chatCameraMemory'
import { framingKeyOf } from './framingKey'
import { viewerSession } from './viewerSession'

const PLACE = { latitude: 25.2523, longitude: 55.3034 }
const CAMERA = { locationKey: '25.2523,55.3034', latitude: 25.253, longitude: 55.304, zoom: 18.2, heading: 30, tilt: 45 }

let counter = 0
const freshChat = () => `chat-${++counter}`
const saved = (chatId: string) => JSON.parse(localStorage.getItem(`asklucy.camera.${chatId}`)!) as typeof CAMERA

beforeEach(() => {
  localStorage.clear()
  viewerSession.camera = null
  viewerSession.framedLocationKey = null
  useGoogleMapsStore.setState({ map: null, handle: null })
})

afterEach(() => {
  setCameraMemoryChat(null)
  useGoogleMapsStore.setState({ map: null, handle: null })
})

describe('chatCameraMemory', () => {
  it('saves the camera under the current chat', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)

    rememberCamera(CAMERA)

    expect(saved(chatId)).toEqual(CAMERA)
  })

  it('saves nothing before a chat is known', () => {
    rememberCamera(CAMERA)
    expect(localStorage.length).toBe(0)
  })

  it('on reload, opens the map at the saved camera and marks the place as framed, so the default framing leaves it alone', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)

    expect(restoreRememberedCamera(chatId, PLACE)).toBe(true)

    expect(viewerSession.camera).toEqual({ latitude: 25.253, longitude: 55.304, zoom: 18.2, heading: 30, tilt: 45 })
    // Exactly the key ViewerSurface computes once the restored location is set.
    expect(viewerSession.framedLocationKey).toBe(
      framingKeyOf({ source: 'agent', ...PLACE, locationType: null, viewport: null }),
    )
  })

  it('moves a map that is already on screen', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)
    const map = { moveCamera: vi.fn() }
    useGoogleMapsStore.setState({ map: map as never })

    restoreRememberedCamera(chatId, PLACE)

    expect(map.moveCamera).toHaveBeenCalledWith({ center: { lat: 25.253, lng: 55.304 }, zoom: 18.2, heading: 30, tilt: 45 })
  })

  it('does not put a camera back over a different place, which keeps its own framing', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)

    expect(restoreRememberedCamera(chatId, { latitude: 25.1972, longitude: 55.2744 })).toBe(false)

    expect(viewerSession.camera).toBeNull()
    expect(viewerSession.framedLocationKey).toBeNull()
  })

  it('does nothing for a chat with nothing saved, and ignores a damaged entry', () => {
    expect(restoreRememberedCamera(freshChat(), PLACE)).toBe(false)

    const bad = freshChat()
    localStorage.setItem(`asklucy.camera.${bad}`, '{"zoom":"x"}')
    expect(restoreRememberedCamera(bad, PLACE)).toBe(false)
  })

  it('restores a chat once per page load', () => {
    const chatId = freshChat()
    setCameraMemoryChat(chatId)
    rememberCamera(CAMERA)

    expect(restoreRememberedCamera(chatId, PLACE)).toBe(true)
    expect(restoreRememberedCamera(chatId, PLACE)).toBe(false)
  })
})
