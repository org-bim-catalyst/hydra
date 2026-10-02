import { useGoogleMapsStore } from '../store/googleMapsStore'

/**
 * Remembers where the user left the map for each chat, across page reloads. `viewerSession.camera` only
 * survives navigation inside one page load; a reload used to open every chat at the default framing of its
 * site. The camera is saved when the map settles and put back once, after the chat's site has been restored
 * (which moves the map itself), unless the user has already taken the map over.
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

/** How long after the restore starts the saved camera keeps being re-applied: the site's own framing arrives late and would otherwise win. */
const RESTORE_TIMEOUT_MS = 5000

let currentChatId: string | null = null
let restoring = false
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

/** Saves the camera for the current chat. Skipped while a saved one is still being put back. */
export function rememberCamera(camera: RememberedCamera) {
  if (!currentChatId || restoring) return
  try {
    localStorage.setItem(keyOf(currentChatId), JSON.stringify(camera))
  } catch (error) {
    // Storage full or blocked: the map still works, it just won't be remembered across a reload.
    console.warn('Could not remember the map position', error)
  }
}

/** Puts the chat's saved camera back, once per chat per page load. A drag or wheel zoom from the user ends it at once. */
export function restoreRememberedCamera(chatId: string, locationKey: string | null) {
  if (restoredChats.has(chatId)) return
  restoredChats.add(chatId)

  const saved = load(chatId)
  // Another place was confirmed since (a turn that finished after the page was left): its framing wins.
  if (!saved || saved.locationKey !== locationKey) return

  restoring = true
  let current: google.maps.Map | null = null
  let listeners: google.maps.MapsEventListener[] = []
  let wheelTarget: HTMLElement | null = null
  let unsubscribe: (() => void) | null = null

  const finish = () => {
    if (!restoring) return
    restoring = false
    detach()
    unsubscribe?.()
    clearTimeout(timer)
  }
  const detach = () => {
    listeners.forEach((l) => l.remove())
    listeners = []
    wheelTarget?.removeEventListener('wheel', finish)
    wheelTarget = null
  }
  const apply = () =>
    current?.moveCamera({
      center: { lat: saved.latitude, lng: saved.longitude },
      zoom: saved.zoom,
      heading: saved.heading,
      tilt: saved.tilt,
    })

  /** The map can be rebuilt while the page settles (theme, map id): follow whichever one is current. */
  const attach = (map: google.maps.Map | null) => {
    if (!restoring || map === current) return
    detach()
    current = map
    if (!map) return

    apply()
    listeners = [map.addListener('idle', apply), map.addListener('dragstart', finish)]
    // A wheel zoom is the user taking the map over, too.
    wheelTarget = map.getDiv?.() ?? null
    wheelTarget?.addEventListener('wheel', finish, { passive: true })
  }

  attach(useGoogleMapsStore.getState().map)
  unsubscribe = useGoogleMapsStore.subscribe((state) => attach(state.map))
  const timer = setTimeout(finish, RESTORE_TIMEOUT_MS)
}
