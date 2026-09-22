import { Loader } from '@googlemaps/js-api-loader'
import * as THREE from 'three'
import type { MapStyleId } from '../../api/commands'
import { worldToLocal } from '../../api/coordinateFrame'
import { sceneAnchor } from '../../scene/SceneAnchor'
import { activeScene } from '../../scene/activeScene'
import { drawingSpaceRegistry } from '../../scene/DrawingSpaceRegistry'
import { redrawScheduler } from '../../scene/RedrawScheduler'
import { rendererState } from '../../scene/rendererState'
import { BUILDING_FOOTPRINT_FILL_COLOR, BUILDING_FOOTPRINT_STROKE_COLOR } from './buildingFootprintColors'
import { createSiteBoundaryRenderer } from './SiteBoundaryRenderer'
import type { BorderConfidenceLevel } from '../../effects/AnimatedBorderHighlight'

export interface GoogleMapsGisLayerOptions {
  apiKey: string
  container: HTMLElement
  /** The active location — where the current-location marker is planted and the scene anchor
   * falls back to. Not necessarily where the camera opens: see `cameraStart`. */
  center: { latitude: number; longitude: number }
  /** Where the camera opens, when that differs from `center`. `MapRenderTarget` passes the
   * previous map's live position here so a recreation (theme toggle, Map ID change, returning to
   * the route) reopens where the user left off. Kept separate from `center` because the marker
   * marks a place in the world, not wherever the camera happens to be pointing — passing the
   * restored position as `center` planted the marker at it, moving "your location" to a spot the
   * user had merely been looking at. */
  cameraStart?: { latitude: number; longitude: number }
  zoom?: number
  /** A vector-rendering-enabled Map ID from Google Cloud Console (Maps Platform → Map
   * Management), with "Tilt" and "Rotation" turned on for that Map ID. `WebGLOverlayView`
   * (and any tilt/heading camera control) only works on a *vector* map — omitting this, or
   * supplying a Map ID that isn't vector-enabled, silently degrades to a flat raster map with
   * no 3D bridging (Google's own SDK logs a clear console warning when that happens; this
   * layer doesn't duplicate that warning). Never hardcode a fake value here — an invalid Map
   * ID produces a real `InvalidKeyMapError`, which is worse than omitting it. */
  mapId?: string
  /** The map style the map should *open* at. Applying a style after construction is not
   * equivalent: Google's `setOptions({mapTypeId})` resets the vector camera — heading and tilt
   * to 0 and fractional zoom snapped to the nearest whole level — so a map built at the default
   * style and corrected a moment later loses whatever camera it was just restored to. Passing
   * the style here means `setMapTypeId` is only ever called for a genuine style *change*. */
  mapStyle?: MapStyleId
  /** FR-005a/SC-004a (research.md Decision, T032a): starts with reduced overlay complexity
   * and auto-rotation paused on detected low-end/mobile devices. */
  reducedQuality: boolean
  /** Base-map color scheme (Google's `colorScheme` MapOption — light/dark road, water, and
   * label colors on the Maps tiles themselves), matching the app's own light/dark theme.
   * Only has a visible effect on a vector map (i.e. when `mapId` is set to a Map ID that isn't
   * pinned to a custom JSON style in Cloud Console) — silently ignored on a raster map.
   * Per `@types/google.maps`' `MapOptions.colorScheme` doc, "this option can only be set when
   * the map is initialized" — there is no live `map.setOptions({colorScheme})` path, so
   * `MapRenderTarget` reacts to a theme toggle by recreating this whole layer rather than by
   * calling a setter here. */
  colorScheme?: MapColorScheme
  /** Initial camera heading/tilt in degrees (defaults: heading 0 = north-up, tilt 45 = the
   * isometric default — `CAMERA_VIEW_MODE_TILT.isometric`). `MapRenderTarget` passes the
   * previous map's live values here across a theme-toggle-triggered recreation, so the camera
   * angle the user was looking from survives instead of resetting on every toggle. */
  heading?: number
  tilt?: number
  onLoaded?: () => void
}

/** Mirrors `store/themeStore.ts`'s `ThemeMode` — kept as a separate type (not imported) so this
 * viewer/ layer stays decoupled from the app-level theme store's module. */
export type MapColorScheme = 'light' | 'dark'

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
  /** One atomic camera write for the fields given — the form `CameraRestoreGuard` uses to put a
   * restored camera back after Maps JS's post-construction initialisation has overwritten it. */
  setCamera(camera: { zoom?: number; heading?: number }): void
  /** Switches the map's base rendering style — `map.setMapTypeId(google.maps.MapTypeId.*)`. */
  setMapTypeId(mapStyle: MapStyleId): void
  /** US5 (FR-018): the current-location marker's `elementId`, for `viewerEngine.registerSelectableElement`. */
  currentLocationMarkerId: string
  /** US5 (FR-018): visually distinguishes the marker as selected/unselected. */
  setMarkerHighlighted(highlighted: boolean): void
  /** specs/042-site-boundary-resolution: shows/updates/clears the animated site-boundary highlight. Pass `null` to remove it. */
  setSiteBoundary(input: { exteriorRing: { latitude: number; longitude: number }[]; confidenceLevel: BorderConfidenceLevel } | null): void
  /** T051 (specs/051 US4) — advances the site-boundary comet animation's internal clock by
   * `deltaSeconds`. Called from `siteBoundaryExtension.tsx`'s own `context.onFrame()`
   * subscription, not automatically every draw — the animation only ticks while that extension
   * is started and keeps requesting a redraw (FR-022). A no-op while no boundary is shown. */
  advanceSiteBoundaryAnimation(deltaSeconds: number): void
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

/** specs/048-buildings-only-map-style: the `'buildings-only'` `MapStyleId`'s custom JSON
 * styling, hiding every category that competes with building footprints for attention. Plain
 * data — safe at module scope, unlike `MAP_STYLE_TO_GOOGLE_TYPE_ID` which needs `google.maps.*`
 * enum values that only exist after the Maps script has loaded.
 *
 * The `landscape.man_made` rule paints building footprints with `BUILDING_FOOTPRINT_*_COLOR` —
 * the same fixed colors the cloud-configured vector-map styles use (see
 * `buildingFootprintColors.ts`'s doc comment) — so a future footprint-extraction algorithm can
 * color-key against one constant regardless of whether raster or vector rendering served the
 * tile it's reading. */
export const BUILDINGS_ONLY_STYLE: google.maps.MapTypeStyle[] = [
  { featureType: 'road', stylers: [{ visibility: 'off' }] },
  { featureType: 'poi', stylers: [{ visibility: 'off' }] },
  { featureType: 'transit', stylers: [{ visibility: 'off' }] },
  { featureType: 'administrative', stylers: [{ visibility: 'off' }] },
  { featureType: 'landscape.natural', stylers: [{ visibility: 'off' }] },
  { featureType: 'landscape.man_made', elementType: 'geometry.fill', stylers: [{ color: BUILDING_FOOTPRINT_FILL_COLOR }] },
  { featureType: 'landscape.man_made', elementType: 'geometry.stroke', stylers: [{ color: BUILDING_FOOTPRINT_STROKE_COLOR }] },
]

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

  // Built here (not module scope) — `google.maps.MapTypeId` only exists once the Maps script has
  // loaded, which the `importLibrary` calls above have already awaited.
  // specs/048-buildings-only-map-style: 'buildings-only' isn't a real MapTypeId — it rides on
  // ROADMAP with BUILDINGS_ONLY_STYLE layered on top.
  const MAP_STYLE_TO_GOOGLE_TYPE_ID: Record<MapStyleId, google.maps.MapTypeId> = {
    roadmap: google.maps.MapTypeId.ROADMAP,
    satellite: google.maps.MapTypeId.SATELLITE,
    hybrid: google.maps.MapTypeId.HYBRID,
    'buildings-only': google.maps.MapTypeId.ROADMAP,
  }

  /** The `MapOptions` for a style, shared by construction and `setMapTypeId` so both express the
   * style the same way. `styles` is omitted entirely on a vector deployment: Google ignores a
   * client-side `styles` array when a Map ID is present and logs a warning for every call
   * (specs/048 research Decision 4 — buildings-only comes from a second cloud-styled Map ID
   * there, not from this array). */
  const mapStyleOptions = (mapStyle: MapStyleId): google.maps.MapOptions =>
    options.mapId
      ? { mapTypeId: MAP_STYLE_TO_GOOGLE_TYPE_ID[mapStyle] }
      : {
          mapTypeId: MAP_STYLE_TO_GOOGLE_TYPE_ID[mapStyle],
          styles: mapStyle === 'buildings-only' ? BUILDINGS_ONLY_STYLE : [],
        }

  const cameraStart = options.cameraStart ?? options.center
  const map = new Map(options.container, {
    ...(options.mapStyle ? mapStyleOptions(options.mapStyle) : {}),
    center: { lat: cameraStart.latitude, lng: cameraStart.longitude },
    zoom: options.zoom ?? 15,
    tilt: options.tilt ?? 45,
    heading: options.heading ?? 0,
    ...(options.mapId ? { mapId: options.mapId } : {}),
    // Vector is requested explicitly, never inherited from the Map ID's Cloud Console
    // configuration. Without this the deployment silently depends on a console setting no code
    // or test can see: a Map ID configured as Raster still constructs, still accepts
    // `setTilt`/`setHeading` and still reports them back, but renders raster -- which supports
    // neither tilt nor a free heading. The camera this app restores on every mount was being
    // dismantled in three steps as Maps JS reconciled it with those limits (heading snapped to
    // the nearest 90 degrees, then tilt forced to 0, then heading forced to 0, accompanied by
    // Google's own "45 degree imagery on raster maps is no longer available" notice).
    ...(options.mapId ? { renderingType: google.maps.RenderingType.VECTOR } : {}),
    colorScheme:
      options.colorScheme === 'dark' ? google.maps.ColorScheme.DARK : google.maps.ColorScheme.LIGHT,
    disableDefaultUI: true,
    gestureHandling: 'greedy',
    // Tilt stays off (adae059d): Google's vector map lets a gesture tilt the camera outside the
    // app's own Isometric/Plan control, and its 45°-imagery auto-engagement compounds that — the
    // `tilt_changed` listener in MapRenderTarget actively corrects both, so allowing the gesture
    // would only mean fighting it. Heading is different: nothing reasserts it the way view mode
    // reasserts tilt, and `heading_changed` already announces it to `viewerEngineStore`, so
    // rotating by hand stays in sync. Turning it off took away the only way to rotate the camera
    // directly, which is worth more here than uniformity between the two flags.
    tiltInteractionEnabled: false,
    headingInteractionEnabled: true,
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

  // research D3/T037 (specs/051) — publishes this scene for viewer-owned content (loadContent's
  // model roots) and capability-owned drawing spaces to be added into. One scene, one binding.
  activeScene.bind(scene)
  drawingSpaceRegistry.bind(scene)


  // specs/042-site-boundary-resolution: added to `scene` once; contents are swapped internally
  // by setSiteBoundary(). clock drives the comet animation from onDraw.
  const siteBoundaryRenderer = createSiteBoundaryRenderer()
  scene.add(siteBoundaryRenderer.object3D)
  const siteBoundaryClock = new THREE.Clock()

  // research D2/FR-008 (specs/051): the viewer owns exactly one reference point, published via
  // `scene/SceneAnchor.ts` and consumed through `api/coordinateFrame.ts`'s `worldToLocal`. This
  // layer's own creation is, today, the first content load — the place that reference point gets
  // set. This replaces the previous per-file re-anchoring bug (specs/042/044 history): the old
  // code re-anchored a private closure variable to each new site boundary's own centroid on every
  // `setSiteBoundary()` call, which was a second, capability-specific reference point living
  // inside this one file — exactly what FR-008 now forbids. `worldToLocal`'s accuracy is stated as
  // within 1 metre at the working scale this feature targets (SC-002), not unlimited-precision
  // geodesy, so this is a deliberate, reviewed trade-off, not an oversight.
  //
  // Found live (2026-09-14): this used to set the anchor unconditionally. Recreating this layer —
  // a theme toggle, a Map ID change, returning to the workspace route — passes the CURRENT camera
  // centre as `options.center`, so every recreation silently moved the anchor to wherever the
  // camera happened to be, while geometry built against the previous anchor stayed where it was.
  // That is why a theme toggle appeared to "fix" the dome. The anchor is set here only when
  // nothing has set it yet; after that it moves only with the active location
  // (viewer/session/anchorFollowsActiveLocation.ts).
  if (!sceneAnchor.get()) sceneAnchor.set(options.center)

  // specs/042-site-boundary-resolution: a plain google.maps.Polygon is the RELIABLE boundary
  // shape — native Maps JS rendering, no dependency on the WebGLOverlayView/Three.js bridge
  // (whose "not runtime-verified" status is documented on this function). The animated highlight
  // (siteBoundaryRenderer above) still layers on top when the bridge is working; if it isn't, the
  // user still sees a clearly recognizable boundary via this polygon alone (FR-002).
  let boundaryPolygon: google.maps.Polygon | undefined
  const BOUNDARY_STYLE: Record<BorderConfidenceLevel, { color: string; fillOpacity: number; strokeOpacity: number; strokeWeight: number }> = {
    // medium/high: a native vector overlay like this Polygon composites above the
    // WebGLOverlayView canvas the rotating border ring draws into, so a bold native stroke here
    // visually competes with (and can mostly hide) that ring rather than sitting under it. Thinned
    // to a faint fallback line — still enough to mark the boundary if the WebGL bridge ever fails
    // to render, but no longer the dominant visual once the ring does render.
    high: { color: '#9C62DE', fillOpacity: 0.18, strokeOpacity: 0.35, strokeWeight: 1 },
    medium: { color: '#9C62DE', fillOpacity: 0.14, strokeOpacity: 0.35, strokeWeight: 1 },
    // low has no WebGL ring to defer to — this IS the primary boundary indicator, full strength.
    low: { color: '#757575', fillOpacity: 0.08, strokeOpacity: 0.7, strokeWeight: 3 },
  }

  // Heading state managed as a simple closure variable — setHeading (called from
  // RotationDriver's RAF loop) stores the value here; onDraw applies it to the Maps camera
  // once per draw cycle so there is no competing RAF loop calling moveCamera directly. This
  // eliminates the frame-contention that caused dropped frames during continuous rotation.
  // Seeded from the heading the map is actually being constructed at, not 0. Left at 0 while the
  // map opened at a restored heading, these two agreed with each other but not with the map, so
  // `onDraw` below saw nothing to apply and the first real rotation frame jumped the camera.
  let desiredHeading = options.heading ?? 0
  let appliedHeading = desiredHeading

  const overlay = new google.maps.WebGLOverlayView()

  // Google's WebGLOverlayView contract requires all four lifecycle callbacks — its own
  // internals call `onAdd`/`onRemove` unconditionally on mount/teardown. Omitting `onRemove`
  // previously threw "onRemove is not a function" as an unhandled rejection during cleanup.
  overlay.onAdd = () => {}
  overlay.onRemove = () => {}

  // Tracks the canvas size the renderer's viewport was last set to (device pixels, since
  // `setPixelRatio(1)` means CSS pixels === device pixels here) — see the `setSize` calls below.
  let lastCanvasWidth = 0
  let lastCanvasHeight = 0

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
    //
    // Always 1, not conditional on reducedQuality/devicePixelRatio (confirmed via a live A/B
    // test, 2026-09-07: forcing 1 unconditionally took AiPresenceCard's sphere -- a separate
    // component sharing this page's GPU -- from a sustained 28-36fps to a steady 64fps on an
    // RTX 4060, with no visible quality loss on either that machine or an RTX 3080; the prior
    // min(devicePixelRatio, 2) upper bound was the actual dominant GPU cost on this page, not
    // the sphere's own rendering, which had already been cut twice with no measurable effect).
    // shouldReduceMapQuality() only checks viewport width, never GPU capability, so on any
    // desktop-width window this renderer previously always ran at that full, uncapped
    // resolution regardless of the GPU's real capability. What this renders -- the animated
    // site-boundary highlight ring (siteBoundaryRenderer below), not the map's own basemap
    // tiles, which Google's native renderer draws separately at full quality -- doesn't need
    // more than 1x to read clearly, so there's no real quality/cost tradeoff being made here.
    renderer.setPixelRatio(1)
    // FOUND LIVE (2026-09-13): nothing ever called `setSize`, so the renderer's internal viewport
    // came only from whatever `gl.canvas.width`/`.height` happened to be at this exact moment —
    // which, on this page's very first load, can still be the canvas's pre-layout placeholder
    // size (React/flex layout hasn't necessarily settled yet when this fires). Three.js caches
    // that viewport and never re-reads the canvas on its own, so every subsequent render drew
    // into that stale, wrong-sized region for the rest of the session: content was genuinely
    // being drawn, just squeezed into a sliver that read as "nothing renders." A later event that
    // reconstructs this whole layer (a theme toggle recreates the map — see MapRenderTarget.tsx's
    // comment on `colorScheme`) fires a *fresh* `onContextRestored` well after layout has
    // settled, which is why toggling the theme "fixed" it — coincidentally, not because the
    // toggle itself did anything relevant. The `false` third argument is required: it updates
    // Three's internal size tracking without touching the canvas's CSS size/style, which Maps
    // owns exclusively.
    renderer.setSize(gl.canvas.width, gl.canvas.height, false)
    lastCanvasWidth = gl.canvas.width
    lastCanvasHeight = gl.canvas.height

    // research D5/FR-018 (specs/051): the one-time color/lighting treatment. The scene previously
    // declared no color-space or tone-mapping handling at all — a correctness gap independent of
    // any capability. Reviewed against SiteBoundaryRenderer's colors (`BOUNDARY_STYLE` below) is
    // an open item recorded in quickstart.md Scenario 9 — this code change is made, the visual
    // before/after comparison itself needs a human with a live browser.
    renderer.outputColorSpace = THREE.SRGBColorSpace
    renderer.toneMapping = THREE.ACESFilmicToneMapping

    // research D4 (specs/051): the map bridge's own `requestRedraw` is the one real redraw call
    // in this application — bound here so `RedrawScheduler.invalidate()` is the only sanctioned
    // path to it (FR-020).
    redrawScheduler.bind(() => overlay.requestRedraw())
    // The conflict-report callback is bound here as a no-op and rebound to the real viewer event
    // bus by `MapRenderTarget.tsx` (which holds the `viewerEngine` reference this layer
    // deliberately does not depend on) immediately after this layer resolves.
    rendererState.bind(renderer, () => {})
  }

  overlay.onDraw = ({ transformer, gl }) => {
    // Companion to the `setSize` fix in `onContextRestored` above: Maps can resize its own
    // canvas after context creation (a container layout change, a window resize) with no signal
    // to this bridge beyond the canvas element's own width/height changing — so this checks on
    // every draw, not just once. Cheap (two integer comparisons) when nothing changed.
    if (gl.canvas.width !== lastCanvasWidth || gl.canvas.height !== lastCanvasHeight) {
      renderer?.setSize(gl.canvas.width, gl.canvas.height, false)
      lastCanvasWidth = gl.canvas.width
      lastCanvasHeight = gl.canvas.height
    }


    // Apply any pending heading update here — inside the Maps SDK draw cycle — so rotation
    // is always synchronised with the Maps renderer. Only calls moveCamera when the heading
    // has actually changed, avoiding redundant camera updates on frames where rotation is off.
    if (desiredHeading !== appliedHeading) {
      map.moveCamera({ heading: desiredHeading })
      appliedHeading = desiredHeading
    }
    // research D2/FR-008 (specs/051): reads the current published reference point fresh every
    // call — never a stale local copy — so it reflects whatever `SceneAnchor.set()` last set,
    // including a change made after this layer was created.
    const reference = sceneAnchor.get() ?? options.center
    const matrix = transformer.fromLatLngAltitude({
      lat: reference.latitude,
      lng: reference.longitude,
      altitude: 0,
    })
    camera.projectionMatrix = new THREE.Matrix4().fromArray(matrix)

    // Closes the coalescing window BEFORE the frame callbacks run, not after. Clearing it
    // afterwards (in a `finally`) meant every `invalidate()` raised from inside a frame callback
    // hit `RedrawScheduler.pending === true` and was silently dropped — so an animation that keeps
    // itself alive by invalidating once per frame (the site-boundary comet, solar playback) died
    // after a single draw, and any capability that built geometry while a redraw was already in
    // flight never got a draw of its own. That is the "nothing renders until you interact with the
    // map" symptom: only an external event (pan, zoom, theme toggle) could request the next frame.
    // Clearing first also means a throw below still leaves the window open rather than wedged.
    redrawScheduler.frameRendered()

    try {
      // specs/042-site-boundary-resolution diagnostic: this Three.js/WebGLOverlayView bridge
      // was never runtime-verified before this feature (see this function's own doc comment).
      // google.maps.WebGLOverlayView appears to swallow exceptions thrown from onDraw silently
      // (no console error, nothing rendered) — wrapped so a real bug here becomes visible
      // instead of looking identical to "nothing to render".
      // The border ring's on-screen width is kept constant across zoom via the standard Web
      // Mercator ground-resolution formula (same "meters per pixel at this zoom/latitude" figure
      // used to size map tiles themselves) — a fixed real-world half-width, even one scaled to
      // the boundary's own size, still only reads correctly at one particular zoom.
      const delta = siteBoundaryClock.getDelta()
      // T051 (specs/051 US4): the comet animation's own tick is no longer driven directly here —
      // it moved to a `context.onFrame()` subscription owned by `siteBoundaryExtension.tsx`,
      // which calls `advanceSiteBoundaryAnimation` (below) and keeps itself looping via
      // `invalidate()` while active — the correct, capability-owned way to keep an animation
      // running under FR-022's "no redraw when nothing changed" rule. `invokeFrameCallbacks`
      // fires that subscription (and any other capability's) once per actual draw — contained
      // per-subscriber (FR-019/T009a) so one capability's failure never stops another's or this
      // draw itself.
      drawingSpaceRegistry.invokeFrameCallbacks(delta)
      // `autoClear = false` deliberately leaves the *color* buffer alone (Maps' own basemap
      // draw for this frame already painted it — clearing color would erase the map). But that
      // also skips clearing *depth*, and the map's camera moves every frame (pan/zoom/rotate),
      // so without this, every draw depth-tests new geometry against the PREVIOUS frame's
      // now-stale depth values at the old camera position — silently failing the depth test
      // almost everywhere after the first frame or two. This is why nothing thrown here ever
      // showed up as an error: the scene renders "successfully," it's just invisible. Clearing
      // only depth (not color) keeps the map intact while giving this frame's geometry a fair
      // depth comparison.
      renderer?.clearDepth()
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

  // Diagnostic workaround: on at least one GPU/driver combination (reported on an RTX 4060,
  // not reproduced on an RTX 3080), updating/removing boundaryPolygon can leave a stale,
  // larger "ghost" of an earlier polygon fill composited into Google's own vector-map render
  // target — it only clears when something forces the browser to fully reallocate/repaint that
  // surface. Two weaker attempts were tried live and confirmed insufficient:
  //   1. map.setZoom(map.getZoom()) — a zero-op zoom nudge.
  //   2. google.maps.event.trigger(map, 'resize') — a *notification* that a resize happened.
  // Neither actually changes the container's real layout size, so nothing ever tells the
  // rendering pipeline (or Google's own resize handling) that anything changed — a synthetic
  // event isn't the same as a real resize. Opening DevTools clears the ghost because it
  // genuinely shrinks the viewport. This reproduces that directly: nudge `right` (not `width`,
  // which would fight the container's own `inset: 0` positioning — see MapRenderTarget.tsx) by
  // 1px, let the browser actually render that frame, then restore it on the next frame — a real,
  // if imperceptible, resize the map's renderer has to process, not a notification it can ignore.
  function nudgeMapRepaint() {
    const container = options.container
    container.style.right = '1px'
    requestAnimationFrame(() => {
      container.style.right = ''
      google.maps.event.trigger(map, 'resize')
    })
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
    setCamera: (camera) => {
      // Written straight through `moveCamera` rather than deferred to `onDraw` like `setHeading`:
      // a restore has to land on the settle it was triggered by, and unlike continuous rotation
      // it happens a handful of times, so it cannot contend for frames. The heading bookkeeping
      // is kept in step so the rotation driver's next frame continues from here instead of
      // replaying a stale heading.
      map.moveCamera(camera)
      if (camera.heading !== undefined) {
        desiredHeading = camera.heading
        appliedHeading = camera.heading
      }
    },
    // specs/048-buildings-only-map-style: setOptions (not the narrower setMapTypeId) — leaving
    // 'buildings-only' for any other style must clear its `styles` array, not just change the
    // base MapTypeId, or the hidden categories would linger.
    //
    // Callers must only invoke this for a real style *change*: on a vector map Google treats a
    // mapTypeId assignment as a camera-affecting event, resetting heading and tilt to 0 and
    // snapping fractional zoom to a whole level. `mapStyle` in the options above exists so
    // construction never has to route through here.
    setMapTypeId: (mapStyle) => map.setOptions(mapStyleOptions(mapStyle)),
    setMarkerHighlighted: (highlighted) => {
      pin.background = highlighted ? '#FBBC04' : '#4285F4'
      pin.scale = highlighted ? 1.3 : 1
    },
    setSiteBoundary: (input) => {
      if (!input) {
        boundaryPolygon?.setMap(null)
        boundaryPolygon = undefined
        nudgeMapRepaint()
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
          strokeWeight: style.strokeWeight,
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
          strokeWeight: style.strokeWeight,
          fillColor: style.color,
          fillOpacity: style.fillOpacity,
        })
        boundaryPolygon.setMap(map)
      }
      nudgeMapRepaint()

      // The bonus path — best-effort animated highlight via the Three.js bridge. Wrapped so a
      // failure here never affects the reliable google.maps.Polygon above (see this function's
      // own doc comment on the bridge's unverified status).
      try {
        // research D2/FR-008 (specs/051): converts against the one published reference point —
        // never re-anchors it to this boundary's own centroid, which was the exact per-capability
        // reference-point violation this feature removes (see the `sceneAnchor.set()` call
        // above). `worldToLocal`'s 1-metre tolerance (SC-002) covers this boundary's own scale.
        const localRing = input.exteriorRing.map((p) => {
          const local = worldToLocal(p, 0)
          return { x: local.x, y: local.y }
        })
        siteBoundaryRenderer.setPolygon(localRing, input.confidenceLevel)
      } catch (error) {
        console.error('[GoogleMapsGisLayer] Failed to build the Three.js site-boundary highlight (native polygon above still shows the boundary):', error)
      }
    },
    advanceSiteBoundaryAnimation: (deltaSeconds) => {
      // Same metersPerPixel formula onDraw itself used before T051's migration — kept here since
      // it needs live zoom/reference state this closure owns.
      const reference = sceneAnchor.get() ?? options.center
      const zoom = map.getZoom() ?? options.zoom ?? 15
      const metersPerPixel = (156_543.03392 * Math.cos((reference.latitude * Math.PI) / 180)) / 2 ** zoom
      siteBoundaryRenderer.update(deltaSeconds, metersPerPixel)
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
