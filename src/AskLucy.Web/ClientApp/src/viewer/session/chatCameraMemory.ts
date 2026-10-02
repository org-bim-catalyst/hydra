import { useGoogleMapsStore } from '../store/googleMapsStore'

/**
 * Remembers where the user left the map for each chat, across page reloads. `viewerSession.camera` only
 * survives navigation inside one page load; a reload used to open every chat at the default framing of its
 * site. The camera is saved when the map settles and put back once, after the chat's site has been restored
 * (which moves the map itself), unless the user has already taken the map over.
 */

interface RememberedCamera {
  latitude: number
  longitude: number
  zoom: number
  heading: number
  tilt: number
}

const keyOf = (chatId: string) => `asklucy.camera.${chatId}`

/** How many settles after the restore the saved camera is re-applied, since restoring the site moves the map too. */
const RESTORE_SETTLES = 2
const RESTORE_TIMEOUT_MS = 6000

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
export function restoreRememberedCamera(chatId: string) {
  if (restoredChats.has(chatId)) return
  restoredChats.add(chatId)

  const saved = load(chatId)
  if (!saved) return

  const begin = (map: google.maps.Map) => {
    restoring = true
    const listeners: google.maps.MapsEventListener[] = []
    let settles = 0

    // eslint-disable-next-line prefer-const -- assigned after finish() is defined, which reads it
    let timer: ReturnType<typeof setTimeout> | undefined
    const finish = () => {
      if (!restoring) return
      restoring = false
      listeners.forEach((l) => l.remove())
      if (timer) clearTimeout(timer)
    }
    const apply = () =>
      map.moveCamera({
        center: { lat: saved.latitude, lng: saved.longitude },
        zoom: saved.zoom,
        heading: saved.heading,
        tilt: saved.tilt,
      })

    apply()
    listeners.push(
      map.addListener('idle', () => {
        settles += 1
        if (settles >= RESTORE_SETTLES) finish()
        else apply()
      }),
      map.addListener('dragstart', finish),
    )
    timer = setTimeout(finish, RESTORE_TIMEOUT_MS)
  }

  const existing = useGoogleMapsStore.getState().map
  if (existing) {
    begin(existing)
    return
  }

  // The map isn't up yet: wait for it, once.
  const unsubscribe = useGoogleMapsStore.subscribe((state) => {
    if (!state.map) return
    unsubscribe()
    begin(state.map)
  })
}
