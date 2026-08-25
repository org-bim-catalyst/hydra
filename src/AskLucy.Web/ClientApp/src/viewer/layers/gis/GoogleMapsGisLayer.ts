import { Loader } from '@googlemaps/js-api-loader'
import * as THREE from 'three'

export interface GoogleMapsGisLayerOptions {
  apiKey: string
  container: HTMLElement
  center: { latitude: number; longitude: number }
  zoom?: number
  /** A vector-rendering-enabled Map ID from Google Cloud Console (Maps Platform → Map
   * Management), with "Tilt" and "Rotation" turned on for that Map ID. `WebGLOverlayView`
   * (and any tilt/heading camera control) only works on a *vector* map — omitting this, or
   * supplying a Map ID that isn't vector-enabled, silently degrades to a flat raster map with
   * no 3D bridging (Google's own SDK logs a clear console warning when that happens; this
   * layer doesn't duplicate that warning). Never hardcode a fake value here — an invalid Map
   * ID produces a real `InvalidKeyMapError`, which is worse than omitting it. */
  mapId?: string
  /** FR-005a/SC-004a (research.md Decision, T032a): starts with reduced overlay complexity
   * and auto-rotation paused on detected low-end/mobile devices. */
  reducedQuality: boolean
  onLoaded?: () => void
}

export interface GoogleMapsGisLayerHandle {
  map: google.maps.Map
  scene: THREE.Scene
  panTo(latitude: number, longitude: number, zoom?: number): void
  /** specs/038-viewer-poi-zoom: fit the camera to show the given bounding box. */
  fitBounds(ne: { lat: number; lng: number }, sw: { lat: number; lng: number }): void
  /** specs/038-viewer-poi-zoom: zoom to a target altitude using a zoom-level approximation. */
  zoomToAltitude(altitudeMetres: number): void
  /** specs/038-viewer-poi-zoom: zoom in or out by one stop (×0.5 / ×2.0 altitude factor). */
  zoomBy(direction: 'in' | 'out'): void
  setHeading(heading: number): void
  setTilt(tilt: number): void
  /** US5 (FR-018): the current-location marker's `elementId`, for `viewerEngine.registerSelectableElement`. */
  currentLocationMarkerId: string
  /** US5 (FR-018): visually distinguishes the marker as selected/unselected. */
  setMarkerHighlighted(highlighted: boolean): void
  dispose(): void
}

/** Matches the mobile-breakpoint convention already used by
 * `features/chat/scene/useSceneQualityTier.ts` (duplicated, not imported — that file drives
 * the separate, protected `AiPresenceCard` scene FR-004 requires stay untouched). */
const MOBILE_BREAKPOINT_PX = 600

/** T032a (FR-005a/SC-004a): whether the map/GIS content mode should render at reduced
 * complexity — narrow/mobile viewports today; `viewer/camera/rotationDriver.ts` (User Story 3)
 * checks the same signal before enabling auto-rotation on a low-end device. */
export function shouldReduceMapQuality(): boolean {
  if (typeof window === 'undefined' || typeof window.matchMedia !== 'function') return false
  return window.matchMedia(`(max-width: ${MOBILE_BREAKPOINT_PX - 0.05}px)`).matches
}

let loaderSingleton: Loader | null = null

function getLoader(apiKey: string): Loader {
  loaderSingleton ??= new Loader({ apiKey, version: 'weekly' })
  return loaderSingleton
}

/** Bridges a Google Maps `WebGLOverlayView` to a Three.js scene (research.md Decision 3, per
 * Google's documented recipe): the overlay owns its own map `<div>`/WebGL context — a
 * `THREE.Scene`/`PerspectiveCamera`/`WebGLRenderer` bound to that context is created in
 * `onContextRestored` and driven by the overlay's own camera transform in `onDraw`. This is
 * loaded lazily (dynamic `import()` at the call site, `MapRenderTarget.tsx`) so its ~large
 * dependency never ships in the initial route bundle (constitution §15).
 *
 * Not runtime-verified in this environment — requires a live, domain-restricted Google Maps
 * Platform API key and a real browser; the shape matches Google's own documented sample. */
export async function createGoogleMapsGisLayer(
  options: GoogleMapsGisLayerOptions,
): Promise<GoogleMapsGisLayerHandle> {
  const loader = getLoader(options.apiKey)
  const { Map } = (await loader.importLibrary('maps')) as google.maps.MapsLibrary
  const { AdvancedMarkerElement, PinElement } = (await loader.importLibrary('marker')) as google.maps.MarkerLibrary

  const map = new Map(options.container, {
    center: { lat: options.center.latitude, lng: options.center.longitude },
    zoom: options.zoom ?? 15,
    tilt: 45,
    ...(options.mapId ? { mapId: options.mapId } : {}),
    disableDefaultUI: true,
    gestureHandling: 'greedy',
  })

  // US5 (FR-018): the current-location marker is this feature's one addressable, selectable
  // element — `ViewerEngine.registerSelectableElement`/`select()` target it by this id.
  const currentLocationMarkerId = 'current-location'
  const pin = new PinElement({ background: '#4285F4', borderColor: '#FFFFFF', glyphColor: '#FFFFFF' })
  const marker = new AdvancedMarkerElement({
    map,
    position: { lat: options.center.latitude, lng: options.center.longitude },
    // `content: pin` (not the deprecated `pin.element`) — PinElement is used directly as of
    // recent Maps JS API versions.
    content: pin,
    title: 'Your current location',
  })

  const scene = new THREE.Scene()
  scene.add(new THREE.AmbientLight(0xffffff, 1))
  const camera = new THREE.PerspectiveCamera()
  let renderer: THREE.WebGLRenderer | undefined

  // Heading state managed as a simple closure variable — setHeading (called from
  // RotationDriver's RAF loop) stores the value here; onDraw applies it to the Maps camera
  // once per draw cycle so there is no competing RAF loop calling moveCamera directly. This
  // eliminates the frame-contention that caused dropped frames during continuous rotation.
  let desiredHeading = 0
  let appliedHeading = 0

  const overlay = new google.maps.WebGLOverlayView()

  // Google's WebGLOverlayView contract requires all four lifecycle callbacks — its own
  // internals call `onAdd`/`onRemove` unconditionally on mount/teardown. Omitting `onRemove`
  // previously threw "onRemove is not a function" as an unhandled rejection during cleanup.
  overlay.onAdd = () => {}
  overlay.onRemove = () => {}

  overlay.onContextRestored = ({ gl }) => {
    renderer = new THREE.WebGLRenderer({
      canvas: gl.canvas as HTMLCanvasElement,
      context: gl,
      ...gl.getContextAttributes(),
    })
    renderer.autoClear = false
    // T032a (FR-005a/SC-004a): a lower pixel ratio on detected low-end/mobile devices is a
    // cheap, broadly effective way to reduce GPU load for a bridged external renderer we
    // don't otherwise control the render loop of.
    renderer.setPixelRatio(options.reducedQuality ? 1 : Math.min(window.devicePixelRatio, 2))
  }

  overlay.onDraw = ({ transformer }) => {
    // Apply any pending heading update here — inside the Maps SDK draw cycle — so rotation
    // is always synchronised with the Maps renderer. Only calls moveCamera when the heading
    // has actually changed, avoiding redundant camera updates on frames where rotation is off.
    if (desiredHeading !== appliedHeading) {
      map.moveCamera({ heading: desiredHeading })
      appliedHeading = desiredHeading
    }
    const matrix = transformer.fromLatLngAltitude({
      lat: options.center.latitude,
      lng: options.center.longitude,
      altitude: 0,
    })
    camera.projectionMatrix = new THREE.Matrix4().fromArray(matrix)
    overlay.requestRedraw()
    renderer?.render(scene, camera)
    renderer?.resetState()
  }

  overlay.setMap(map)
  options.onLoaded?.()

  // specs/038-viewer-poi-zoom: zoom = log2(C / altitude) approximation for Google Maps zoom levels.
  // C ≈ 591 657 550 m is the ground-level circumference represented at zoom 0.
  const ALTITUDE_ZOOM_CONSTANT = 591_657_550
  const ZOOM_MIN = 0
  const ZOOM_MAX = 21

  function altitudeToZoom(altitudeMetres: number): number {
    const clamped = Math.max(50, Math.min(500_000, altitudeMetres))
    return Math.max(ZOOM_MIN, Math.min(ZOOM_MAX, Math.log2(ALTITUDE_ZOOM_CONSTANT / clamped)))
  }

  return {
    map,
    scene,
    currentLocationMarkerId,
    // `moveCamera` (not `panTo`/`setHeading`/`setTilt`) — the atomic, single-call form Google's
    // own vector-map/WebGLOverlayView samples use; only the specified fields change.
    panTo: (latitude, longitude, zoom) =>
      map.moveCamera({ center: { lat: latitude, lng: longitude }, ...(zoom !== undefined ? { zoom } : {}) }),
    fitBounds: (ne, sw) => {
      const bounds = new google.maps.LatLngBounds({ lat: sw.lat, lng: sw.lng }, { lat: ne.lat, lng: ne.lng })
      map.fitBounds(bounds)
    },
    zoomToAltitude: (altitudeMetres) => {
      map.moveCamera({ zoom: altitudeToZoom(altitudeMetres) })
    },
    zoomBy: (direction) => {
      const currentZoom = map.getZoom() ?? 15
      const newZoom = direction === 'in'
        ? Math.min(ZOOM_MAX, currentZoom + 1)
        : Math.max(ZOOM_MIN, currentZoom - 1)
      map.moveCamera({ zoom: newZoom })
    },
    setHeading: (heading) => { desiredHeading = heading },
    setTilt: (tilt) => map.moveCamera({ tilt }),
    setMarkerHighlighted: (highlighted) => {
      pin.background = highlighted ? '#FBBC04' : '#4285F4'
      pin.scale = highlighted ? 1.3 : 1
    },
    dispose: () => {
      marker.map = null
      overlay.setMap(null)
      renderer?.dispose()
    },
  }
}
