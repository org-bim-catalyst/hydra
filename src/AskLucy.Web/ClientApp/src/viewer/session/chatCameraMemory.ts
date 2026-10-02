import { viewerEngine } from '../engine/viewerEngineInstance'
import { framingKeyOf } from './framingKey'
import { viewerSession } from './viewerSession'

/**
 * Remembers where the user left the map for each chat, across page reloads. `viewerSession.camera` only
 * survives navigation inside one page load; a reload used to open every chat at the default framing of its
 * site. The camera is saved whenever the map settles, and put back when the chat's site is restored.
 *
 * Putting it back does not fight the default framing after the fact (that raced, and the framing's
 * animation often won). It goes in ahead of it, through the viewer's own session: the camera a map opens at
 * (`viewerSession.camera`) and the location already framed (`viewerSession.framedLocationKey`), so the
 * framing sees the place as done and leaves the camera alone.
 */

/** Identifies the place the camera was looking at: the chat's confirmed location, to about 10 m. */
export const locationKeyOf = (latitude: number | null, longitude: number | null) =>
  latitude === null || longitude === null ? null : `${latitude.toFixed(4)},${longitude.toFixed(4)}`

interface RememberedCamera {
  /** The chat's location when this was saved; a camera for another place is never put back. */
  locationKey: string | null
  latitude: number
  longitude: number
  zoom: number
  heading: number
  tilt: number
}

const keyOf = (chatId: string) => `asklucy.camera.${chatId}`

let currentChatId: string | null = null
const restoredChats = new Set<string>()

const isCamera = (value: unknown): value is RememberedCamera => {
  const c = value as Partial<RememberedCamera> | null
  return !!c && [c.latitude, c.longitude, c.zoom, c.heading, c.tilt].every((n) => typeof n === 'number' && Number.isFinite(n))
}

function load(chatId: string): RememberedCamera | null {
  try {
    const raw = localStorage.getItem(keyOf(chatId))
    const parsed: unknown = raw ? JSON.parse(raw) : null
    return isCamera(parsed) ? parsed : null
  } catch (error) {
    console.warn('Could not read the remembered map position', error)
    return null
  }
}

/** The chat whose camera is being tracked; null before a chat is known, when nothing is saved. */
export function setCameraMemoryChat(chatId: string | null) {
  currentChatId = chatId
}

/** Saves the camera for the current chat. */
export function rememberCamera(camera: RememberedCamera) {
  if (!currentChatId) return
  try {
    localStorage.setItem(keyOf(currentChatId), JSON.stringify(camera))
  } catch (error) {
    // Storage full or blocked: the map still works, it just won't be remembered across a reload.
    console.warn('Could not remember the map position', error)
  }
}

/**
 * Puts the chat's saved camera back, once per chat per page load. Call it before the chat's location is
 * set on `activeLocationStore`: it marks that location as framed, so the default framing then skips it.
 * Returns whether a camera was put back.
 */
export function restoreRememberedCamera(chatId: string, location: { latitude: number; longitude: number }): boolean {
  if (restoredChats.has(chatId)) return false
  restoredChats.add(chatId)

  const saved = load(chatId)
  // Another place was confirmed since (a turn that finished after the page was left): its framing wins.
  if (!saved || saved.locationKey !== locationKeyOf(location.latitude, location.longitude)) return false

  const camera = { latitude: saved.latitude, longitude: saved.longitude, zoom: saved.zoom, heading: saved.heading, tilt: saved.tilt }

  // A map created from here on opens at this camera; and the location about to be set counts as framed.
  viewerSession.camera = camera
  viewerSession.framedLocationKey = framingKeyOf({ source: 'agent', ...location, locationType: null, viewport: null })

  // The map already on screen, or - on a reload - the one still being created, is put there. Since the
  // place is now marked as framed, nothing else would move a map that opened at the device's location.
  viewerEngine.restoreCamera(camera)
  return true
}
