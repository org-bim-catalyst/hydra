import { Loader } from '@googlemaps/js-api-loader'
import * as THREE from 'three'
import type { MapStyleId } from '../../api/commands'
import { createSiteBoundaryRenderer } from './SiteBoundaryRenderer'
import type { BorderConfidenceLevel, LocalPoint } from '../../effects/AnimatedBorderHighlight'

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
  /** Switches the map's base rendering style — `map.setMapTypeId(google.maps.MapTypeId.*)`. */
  setMapTypeId(mapStyle: MapStyleId): void
  /** US5 (FR-018): the current-location marker's `elementId`, for `viewerEngine.registerSelectableElement`. */
  currentLocationMarkerId: string
  /** US5 (FR-018): visually distinguishes the marker as selected/unselected. */
  setMarkerHighlighted(highlighted: boolean): void
  /** specs/042-site-boundary-resolution: shows/updates/clears the animated site-boundary highlight. Pass `null` to remove it. */
  setSiteBoundary(input: { exteriorRing: { latitude: number; longitude: number }[]; confidenceLevel: BorderConfidenceLevel } | null): void
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

/** specs/042-site-boundary-resolution research.md #8 — same equirectangular local-meters
 * projection as the backend's `GeometryMath.ToLocalMeters` (no shared code between the two
 * stacks; kept in sync deliberately, both being small, stable, single-purpose formulas).
 * `reference` is always the layer's own fixed `options.center` — the same real-world anchor
 * `onDraw` already uses for the camera's `transformer.fromLatLngAltitude` call every frame, so
 * anything placed here in local meters tracks correctly with the live Maps camera with no
 * separate per-object transform call needed. */
const METERS_PER_DEGREE_LATITUDE = 111_320

function toLocalMeters(point: { latitude: number; longitude: number }, reference: { latitude: number; longitude: number }): LocalPoint {
  const metersPerDegreeLongitude = METERS_PER_DEGREE_LATITUDE * Math.cos((reference.latitude * Math.PI) / 180)
  return {
    x: (point.longitude - reference.longitude) * metersPerDegreeLongitude,
    y: (point.latitude - reference.latitude) * METERS_PER_DEGREE_LATITUDE,
  }
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

  // specs/042-site-boundary-resolution: added to `scene` once; contents are swapped internally
  // by setSiteBoundary(). clock drives the comet animation from onDraw.
  const siteBoundaryRenderer = createSiteBoundaryRenderer()
  scene.add(siteBoundaryRenderer.object3D)
  const siteBoundaryClock = new THREE.Clock()

  // specs/042-site-boundary-resolution bugfix: MapRenderTarget only creates this layer ONCE
  // (its effect depends on [layerId], a stable constant — `options.center` is whatever location
  // was active at that single mount, never updated on later searches). The camera itself already
  // pans correctly via map.moveCamera() elsewhere, but this closure's own Three.js anchor point
  // does NOT track that — a boundary resolved far from the original mount location was being
  // placed in local-meters space relative to a stale, unrelated anchor. Re-anchored to the
  // boundary's own centroid on every setSiteBoundary() call instead of the frozen options.center
  // — always accurate for whatever is actually being rendered, and sidesteps float precision
  // concerns from a potentially-distant fixed anchor.
  let sceneAnchor = { ...options.center }

  // specs/042-site-boundary-resolution: a plain google.maps.Polygon is the RELIABLE boundary
  // shape — native Maps JS rendering, no dependency on the WebGLOverlayView/Three.js bridge
  // (whose "not runtime-verified" status is documented on this function). The animated comet
  // effect (siteBoundaryRenderer above) still layers on top when the bridge is working; if it
  // isn't, the user still sees a clearly recognizable boundary via this polygon alone (FR-002).
  let boundaryPolygon: google.maps.Polygon | undefined
  const BOUNDARY_STYLE: Record<BorderConfidenceLevel, { color: string; fillOpacity: number; strokeOpacity: number }> = {
    high: { color: '#9C62DE', fillOpacity: 0.18, strokeOpacity: 0.95 },
    medium: { color: '#9C62DE', fillOpacity: 0.14, strokeOpacity: 0.85 },
    low: { color: '#757575', fillOpacity: 0.08, strokeOpacity: 0.7 },
  }

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
      lat: sceneAnchor.latitude,
      lng: sceneAnchor.longitude,
      altitude: 0,
    })
    camera.projectionMatrix = new THREE.Matrix4().fromArray(matrix)
    try {
      // specs/042-site-boundary-resolution diagnostic: this Three.js/WebGLOverlayView bridge
      // was never runtime-verified before this feature (see this function's own doc comment).
      // google.maps.WebGLOverlayView appears to swallow exceptions thrown from onDraw silently
      // (no console error, nothing rendered) — wrapped so a real bug here becomes visible
      // instead of looking identical to "nothing to render".
      siteBoundaryRenderer.update(siteBoundaryClock.getDelta())
      overlay.requestRedraw()
      renderer?.render(scene, camera)
      renderer?.resetState()
    } catch (error) {
      console.error('[GoogleMapsGisLayer] Three.js site-boundary render failed:', error)
    }
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

  // Built here (not module scope) — `google.maps.MapTypeId` only exists once the Maps script
  // has loaded, which `loader.importLibrary` above has already awaited by this point.
  const MAP_STYLE_TO_GOOGLE_TYPE_ID: Record<MapStyleId, google.maps.MapTypeId> = {
    roadmap: google.maps.MapTypeId.ROADMAP,
    satellite: google.maps.MapTypeId.SATELLITE,
    hybrid: google.maps.MapTypeId.HYBRID,
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
    setMapTypeId: (mapStyle) => map.setMapTypeId(MAP_STYLE_TO_GOOGLE_TYPE_ID[mapStyle]),
    setMarkerHighlighted: (highlighted) => {
      pin.background = highlighted ? '#FBBC04' : '#4285F4'
      pin.scale = highlighted ? 1.3 : 1
    },
    setSiteBoundary: (input) => {
      if (!input) {
        boundaryPolygon?.setMap(null)
        boundaryPolygon = undefined
        try {
          siteBoundaryRenderer.setPolygon(null, 'low')
        } catch (error) {
          console.error('[GoogleMapsGisLayer] Failed to clear the Three.js site-boundary highlight:', error)
        }
        return
      }

      // The reliable path — always runs, never depends on the Three.js bridge.
      const style = BOUNDARY_STYLE[input.confidenceLevel]
      const path = input.exteriorRing.map((p) => ({ lat: p.latitude, lng: p.longitude }))
      if (!boundaryPolygon) {
        boundaryPolygon = new google.maps.Polygon({
          map,
          paths: path,
          strokeColor: style.color,
          strokeOpacity: style.strokeOpacity,
          strokeWeight: 3,
          fillColor: style.color,
          fillOpacity: style.fillOpacity,
          clickable: false,
          zIndex: 10,
        })
      } else {
        boundaryPolygon.setPath(path)
        boundaryPolygon.setOptions({
          strokeColor: style.color,
          strokeOpacity: style.strokeOpacity,
          fillColor: style.color,
          fillOpacity: style.fillOpacity,
        })
        boundaryPolygon.setMap(map)
      }

      // The bonus path — best-effort animated highlight via the Three.js bridge. Wrapped so a
      // failure here never affects the reliable google.maps.Polygon above (see this function's
      // own doc comment on the bridge's unverified status).
      try {
        // Re-anchor to this boundary's own centroid (not the frozen options.center — see the
        // sceneAnchor declaration above) before converting to local meters, so precision is
        // always good regardless of how far the map has panned since this layer was created.
        sceneAnchor = {
          latitude: input.exteriorRing.reduce((sum, p) => sum + p.latitude, 0) / input.exteriorRing.length,
          longitude: input.exteriorRing.reduce((sum, p) => sum + p.longitude, 0) / input.exteriorRing.length,
        }
        const localRing = input.exteriorRing.map((p) => toLocalMeters(p, sceneAnchor))
        siteBoundaryRenderer.setPolygon(localRing, input.confidenceLevel)
      } catch (error) {
        console.error('[GoogleMapsGisLayer] Failed to build the Three.js site-boundary highlight (native polygon above still shows the boundary):', error)
      }
    },
    dispose: () => {
      marker.map = null
      overlay.setMap(null)
      boundaryPolygon?.setMap(null)
      siteBoundaryRenderer.dispose()
      renderer?.dispose()
    },
  }
}
