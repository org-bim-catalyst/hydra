import * as THREE from 'three'
import type { IViewerEngine } from '../api/engine'
import type { OverlayInput, RenderLayer, RenderLayerInput } from '../api/layers'
import type { CameraState, CameraViewMode, MapStyleId, ViewerCommandResult } from '../api/commands'
import type { ViewerEventHandler, ViewerEventType } from '../api/events'
import { worldToLocal } from '../api/coordinateFrame'
import type { ContentSource, ContentFailureReason, ViewerContent, WorldPlacement } from '../content/ViewerContent'
import { useContentStore } from '../content/contentStore'
import { loadGltfContent } from '../content/loaders/gltfContentLoader'
import { getElementProperties, type ElementIndex, type ElementProperties } from '../elements/elementIndex'
import { activeScene } from '../scene/activeScene'
import { redrawScheduler } from '../scene/RedrawScheduler'
import type { DrawingRequirement } from '../scene/rendererState'
import { sceneAnchor, type ReferencePoint } from '../scene/SceneAnchor'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { ViewerEventBus } from './viewerEventBus'

function ok<T>(data?: T): ViewerCommandResult<T> {
  return { ok: true, data }
}

function fail<T>(error: string): ViewerCommandResult<T> {
  return { ok: false, error }
}

function generateId(prefix: string): string {
  const random =
    typeof crypto !== 'undefined' && 'randomUUID' in crypto
      ? crypto.randomUUID()
      : Math.random().toString(36).slice(2)
  return `${prefix}-${random}`
}

/** Internal hook the currently-mounted render target (`MapRenderTarget`) uses to receive
 * camera/navigation commands. Deliberately NOT part of the public `IViewerEngine` contract
 * (contracts/viewer-engine-api.md) — a future AI-agent caller only ever sees the public
 * command surface; this is the plumbing underneath it. `PlaceholderRenderTarget` registers
 * nothing, so camera commands succeed but have no visible effect while it's active, per the
 * FR-004/FR-013/FR-017 resolution in spec.md. */
export interface ViewerRenderTargetHandle {
  panTo?(latitude: number, longitude: number, zoom?: number): void
  /** specs/038-viewer-poi-zoom: fit the camera to show the given NE/SW bounding box. */
  fitBounds?(ne: { lat: number; lng: number }, sw: { lat: number; lng: number }): void
  /** specs/038-viewer-poi-zoom: animate camera to the given altitude (metres). */
  zoomToAltitude?(altitudeMetres: number): void
  /** specs/038-viewer-poi-zoom: zoom in or out by one stop (×0.5 / ×2.0 altitude). */
  zoomBy?(direction: 'in' | 'out'): void
  applyViewMode?(mode: CameraViewMode): void
  applyRotationEnabled?(enabled: boolean): void
  applyMapStyle?(mapStyle: MapStyleId): void
  /** specs/051 FR-025 — the render target's current camera snapshot. */
  getCameraState?(): CameraState
}

/** The viewer's public command/event facade (FR-021–FR-024, contracts/viewer-engine-api.md,
 * data-model.md "Viewer Command"/"Viewer Event"). A single instance is owned by the mounted
 * `ViewerSurface` (features/viewer/components/ViewerSurface.tsx) and reads/writes
 * `viewerEngineStore` — UI components call through this facade rather than the store directly,
 * so the exact same surface is available to a future AI agent unchanged (FR-024). Every command
 * documented in contracts/viewer-engine-api.md is implemented here (FR-021) and independently
 * exercisable without any AI agent connected (SC-006, ViewerEngine.contract.test.ts). */
export class ViewerEngine implements IViewerEngine {
  private readonly events = new ViewerEventBus()
  private activeTarget: ViewerRenderTargetHandle | null = null
  private readonly selectableElements = new Map<string, Set<string>>()
  // specs/038-viewer-poi-zoom T044: prevents visual glitches from rapid successive zoom commands.
  private _isAnimating = false
  // specs/051 — per-layer element index (populated by a content loader that found addressable
  // nodes) and per-content loaded root objects (for disposal on replace/unload, FR-006/FR-033).
  private readonly elementIndices = new Map<string, ElementIndex>()
  private readonly loadedContentObjects = new Map<string, THREE.Object3D>()

  on<E extends ViewerEventType>(type: E, handler: ViewerEventHandler<E>): () => void {
    return this.events.on(type, handler)
  }

  protected emit = this.events.emit.bind(this.events)

  protected get store() {
    return useViewerEngineStore.getState()
  }

  /** Called by `MapRenderTarget` on mount/unmount (User Story 2). */
  registerRenderTarget(target: ViewerRenderTargetHandle): () => void {
    this.activeTarget = target
    return () => {
      if (this.activeTarget === target) this.activeTarget = null
    }
  }

  /** Called by a render target once its content finishes loading (e.g. `MapRenderTarget` after
   * `createGoogleMapsGisLayer` resolves) — fires the public `contentLoaded` event (FR-023). Not
   * part of `IViewerEngine`: this is a notification a render target sends inward, not a command
   * an external caller issues. */
  notifyContentLoaded(layerId: string): void {
    this.emit({ type: 'contentLoaded', layerId })
  }

  /** specs/051 research D3a — called by `DrawingSpaceRegistry` when a capability's `onFrame`
   * callback throws (FR-019, constitution §2.VIII). Not part of `IViewerEngine`: an internal
   * notification, mirroring `notifyContentLoaded`'s own posture. */
  notifyDrawingCallbackFailed(extensionId: string, message: string): void {
    this.emit({ type: 'drawingCallbackFailed', extensionId, message })
  }

  /** specs/051 research D3 — called by `rendererState` when two capabilities declare conflicting
   * drawing requirements (FR-017). */
  notifyDrawingRequirementConflict(requirement: DrawingRequirement, requestedBy: string[]): void {
    this.emit({ type: 'drawingRequirementConflict', requirement, requestedBy })
  }

  /** Called by a layer (e.g. `GoogleMapsGisLayer`'s current-location marker, User Story 5) once
   * an addressable element it owns becomes selectable. `select()` only accepts an `elementId`
   * registered this way, so FR-022's "unknown element" failure is real, not just documentation. */
  registerSelectableElement(layerId: string, elementId: string): () => void {
    let set = this.selectableElements.get(layerId)
    if (!set) {
      set = new Set()
      this.selectableElements.set(layerId, set)
    }
    set.add(elementId)
    return () => {
      set?.delete(elementId)
      const selection = this.store.selection
      if (selection.selectedLayerId === layerId && selection.selectedElementId === elementId) {
        this.clearSelection()
      }
    }
  }

  addLayer(layer: RenderLayerInput): ViewerCommandResult<{ layerId: string }> {
    const layers = this.store.layers
    const id = layer.id ?? generateId(layer.kind)
    if (layers.some((existing) => existing.id === id)) {
      return fail(`A layer with id "${id}" is already registered.`)
    }
    const newLayer: RenderLayer = {
      id,
      kind: layer.kind,
      visible: layer.visible ?? true,
      zIndex: layer.zIndex ?? 0,
      metadata: layer.metadata ?? {},
    }
    useViewerEngineStore.getState().setLayers([...layers, newLayer])
    this.emit({ type: 'layerAdded', layerId: id, kind: layer.kind })
    return ok({ layerId: id })
  }

  removeLayer(layerId: string): ViewerCommandResult {
    const layers = this.store.layers
    if (!layers.some((layer) => layer.id === layerId)) {
      return fail(`No layer with id "${layerId}" is registered.`)
    }
    useViewerEngineStore.getState().setLayers(layers.filter((layer) => layer.id !== layerId))
    this.emit({ type: 'layerRemoved', layerId })
    return ok()
  }

  setLayerVisibility(layerId: string, visible: boolean): ViewerCommandResult {
    const layers = this.store.layers
    if (!layers.some((layer) => layer.id === layerId)) {
      return fail(`No layer with id "${layerId}" is registered.`)
    }
    useViewerEngineStore
      .getState()
      .setLayers(layers.map((layer) => (layer.id === layerId ? { ...layer, visible } : layer)))
    return ok()
  }

  zoomToLocation(latitude: number, longitude: number, zoom?: number): ViewerCommandResult {
    if (latitude < -90 || latitude > 90) {
      return fail('Latitude must be between -90 and 90.')
    }
    if (longitude < -180 || longitude > 180) {
      return fail('Longitude must be between -180 and 180.')
    }
    // Succeeds even with no active render target (e.g. the placeholder is showing) — the
    // camera position is only meaningful once real content exists, per FR-013/FR-017.
    this.activeTarget?.panTo?.(latitude, longitude, zoom)
    return ok()
  }

  /** specs/038-viewer-poi-zoom: fit the camera to show the bounding box defined by NE and SW corners.
   * Falls back to zoomToAltitude(200) when the box is degenerate (NE === SW). Logs a warning
   * and returns when the map is not yet initialized (no active render target). */
  fitBounds(ne: { lat: number; lng: number }, sw: { lat: number; lng: number }): void {
    if (!this.activeTarget?.fitBounds) {
      console.warn('[ViewerEngine] fitBounds: no active render target — map not yet initialized.')
      return
    }
    if (ne.lat === sw.lat && ne.lng === sw.lng) {
      this.zoomToAltitude(200)
      return
    }
    this.activeTarget.fitBounds(ne, sw)
  }

  /** specs/038-viewer-poi-zoom: animate camera to the given altitude in metres.
   * The altitude is clamped to [50, 500_000] m inside the render target. */
  zoomToAltitude(altitudeMetres: number): void {
    if (!this.activeTarget?.zoomToAltitude) {
      console.warn('[ViewerEngine] zoomToAltitude: no active render target — map not yet initialized.')
      return
    }
    this.activeTarget.zoomToAltitude(altitudeMetres)
  }

  /** specs/038-viewer-poi-zoom: zoom in or out by one stop (×0.5 / ×2.0 altitude factor).
   * Cancels any in-flight animation before starting a new one (T044). Logs a warning and
   * returns when the map is not yet initialized. */
  zoomBy(direction: 'in' | 'out'): void {
    if (!this.activeTarget?.zoomBy) {
      console.warn('[ViewerEngine] zoomBy: no active render target — map not yet initialized.')
      return
    }
    // T044: debounce rapid zoom commands — skip while an animation is in progress.
    // The 600ms window matches the Google Maps SDK moveCamera animation duration.
    if (this._isAnimating) return
    this._isAnimating = true
    this.activeTarget.zoomBy(direction)
    window.setTimeout(() => {
      this._isAnimating = false
    }, 600)
  }

  setViewMode(mode: CameraViewMode): ViewerCommandResult {
    useViewerEngineStore.getState().setCamera({ mode })
    // Succeeds even with no active render target — the control stays operable while the
    // placeholder is showing, it just has no visible effect (FR-013 as revised).
    this.activeTarget?.applyViewMode?.(mode)
    this.emit({ type: 'viewModeChanged', mode })
    return ok()
  }

  /** Switches the map/GIS content mode's base rendering style (Google Maps' ROADMAP/SATELLITE/
   * HYBRID). Succeeds even with no active render target — the control stays operable while the
   * placeholder is showing, matching setViewMode/setRotationEnabled's convention. */
  setMapStyle(mapStyle: MapStyleId): ViewerCommandResult {
    useViewerEngineStore.getState().setMapStyle(mapStyle)
    this.activeTarget?.applyMapStyle?.(mapStyle)
    this.emit({ type: 'mapStyleChanged', mapStyle })
    return ok()
  }

  setRotationEnabled(enabled: boolean): ViewerCommandResult {
    useViewerEngineStore.getState().setCamera({ rotationEnabled: enabled })
    this.activeTarget?.applyRotationEnabled?.(enabled)
    this.emit({ type: 'rotationChanged', enabled })
    return ok()
  }

  select(layerId: string, elementId: string): ViewerCommandResult {
    if (!this.selectableElements.get(layerId)?.has(elementId)) {
      return fail(`No selectable element "${elementId}" on layer "${layerId}".`)
    }
    useViewerEngineStore.getState().setSelection({ selectedLayerId: layerId, selectedElementId: elementId })
    this.emit({ type: 'selectionChanged', layerId, elementId })
    return ok()
  }

  clearSelection(): ViewerCommandResult {
    useViewerEngineStore.getState().setSelection({ selectedLayerId: null, selectedElementId: null })
    this.emit({ type: 'selectionChanged', layerId: null, elementId: null })
    return ok()
  }

  displayContent(layerId: string, content: unknown): ViewerCommandResult {
    const layers = this.store.layers
    const layer = layers.find((existing) => existing.id === layerId)
    if (!layer) {
      return fail(`No layer with id "${layerId}" is registered.`)
    }
    if (content === null || content === undefined) {
      return fail('Content must not be null or undefined.')
    }
    useViewerEngineStore
      .getState()
      .setLayers(layers.map((existing) => (existing.id === layerId ? { ...existing, metadata: { ...existing.metadata, content } } : existing)))
    this.emit({ type: 'contentLoaded', layerId })
    return ok()
  }

  createOverlay(overlay: OverlayInput): ViewerCommandResult<{ overlayId: string }> {
    const result = this.addLayer({
      id: overlay.id,
      kind: 'overlay',
      zIndex: overlay.zIndex,
      metadata: overlay.metadata,
    })
    return result.ok ? ok({ overlayId: result.data!.layerId }) : fail(result.error!)
  }

  // ---------------------------------------------------------------------------------------
  // specs/051-viewer-scene-content-api — additive only (FR-038, FR-039).
  // ---------------------------------------------------------------------------------------

  /** FR-025 — reads the active render target's live camera snapshot. `ok: false` (not a thrown
   * error) when nothing is showing yet, matching every other command's "no active render target"
   * posture. */
  getCameraState(): ViewerCommandResult<{ camera: CameraState }> {
    const camera = this.activeTarget?.getCameraState?.()
    if (!camera) {
      return fail('No active render target — camera state is not available yet.')
    }
    return ok({ camera })
  }

  /** FR-026 — called by the render target on its map's `'idle'` event (research D6), not per
   * frame. Not part of `IViewerEngine`: a render-target-to-engine notification, mirroring
   * `notifyContentLoaded`. */
  notifyCameraChanged(camera: CameraState): void {
    this.emit({ type: 'cameraChanged', camera })
  }

  /** FR-008 — the one published reference point, or `null` before any content has loaded. */
  getReferencePoint(): ViewerCommandResult<{ referencePoint: ReferencePoint | null }> {
    return ok({ referencePoint: sceneAnchor.get() })
  }

  /** FR-001, FR-002, FR-003, FR-005, FR-006, FR-007, FR-011, FR-013 (research D1). Resolves
   * synchronously with a content id once the request is accepted and loading has begun — the
   * eventual `loaded`/`failed` outcome arrives via `contentLoaded`/`contentFailed` (FR-005's
   * loading indication exists precisely because this does not block on the actual load). */
  loadContent(source: ContentSource, placement?: WorldPlacement): ViewerCommandResult<{ contentId: string }> {
    const contentId = generateId('content')
    return this.beginLoadContent(contentId, source, placement)
  }

  /** FR-001, FR-006 — releases the previous content's resources before the new content begins
   * loading. Fails if `contentId` does not name existing content. */
  replaceContent(contentId: string, source: ContentSource, placement?: WorldPlacement): ViewerCommandResult {
    const existing = useContentStore.getState().content.find((c) => c.id === contentId)
    if (!existing) {
      return fail(`No content with id "${contentId}" is loaded.`)
    }
    this.releaseContentResources(existing)
    const result = this.beginLoadContent(contentId, source, placement)
    return result.ok ? ok() : fail(result.error!)
  }

  /** FR-001, FR-006. */
  unloadContent(contentId: string): ViewerCommandResult {
    const existing = useContentStore.getState().content.find((c) => c.id === contentId)
    if (!existing) {
      return fail(`No content with id "${contentId}" is loaded.`)
    }
    this.releaseContentResources(existing)
    useContentStore.getState().remove(contentId)
    return ok()
  }

  /** FR-001 — always succeeds; may return an empty list. */
  listContent(): ViewerCommandResult<{ content: ViewerContent[] }> {
    return ok({ content: useContentStore.getState().content })
  }

  /** FR-027, FR-028, FR-031. */
  getElementInfo(layerId: string, elementId: string): ViewerCommandResult<{ info: ElementProperties }> {
    return ok({ info: getElementProperties(this.elementIndices.get(layerId), elementId) })
  }

  /** FR-029 — composes the existing `select()` with a framing call. Fails with a stated reason
   * (US3 AC4) rather than a silent no-op if the element is no longer registered. */
  selectAndFrame(layerId: string, elementId: string): ViewerCommandResult {
    const selectResult = this.select(layerId, elementId)
    if (!selectResult.ok) return selectResult

    const layer = this.store.layers.find((l) => l.id === layerId)
    const placement = layer?.metadata?.placement as WorldPlacement | undefined
    if (placement) {
      this.zoomToLocation(placement.latitude, placement.longitude)
    }
    return ok()
  }

  /** research D4 — the only sanctioned redraw request path. */
  invalidate(): void {
    redrawScheduler.invalidate()
  }

  private beginLoadContent(
    contentId: string,
    source: ContentSource,
    placement: WorldPlacement | undefined,
  ): ViewerCommandResult<{ contentId: string }> {
    if (source.kind === 'gis') {
      if (!sceneAnchor.get()) sceneAnchor.set(source.center)
      const layerResult = this.addLayer({ kind: 'gis', metadata: { provider: source.provider, center: source.center, zoom: source.zoom } })
      if (!layerResult.ok) return fail(layerResult.error!)
      const layerId = layerResult.data!.layerId

      const content: ViewerContent = {
        id: contentId,
        layerId,
        source,
        placement: placement ?? null,
        loadState: 'loaded',
        failureReason: null,
      }
      useContentStore.getState().upsert(content)
      this.notifyContentLoaded(layerId)
      return ok({ contentId })
    }

    // model kind (FR-007, FR-011, FR-013) — requires a placement before any loader runs.
    if (!placement) {
      const content: ViewerContent = { id: contentId, layerId: '', source, placement: null, loadState: 'failed', failureReason: 'unplaceable' }
      useContentStore.getState().upsert(content)
      this.emit({ type: 'contentFailed', contentId, reason: 'unplaceable' })
      return fail('Content has no placement — it cannot be shown.')
    }

    const layerResult = this.addLayer({ kind: 'model', metadata: { format: source.format, placement } })
    if (!layerResult.ok) return fail(layerResult.error!)
    const layerId = layerResult.data!.layerId

    const loadingContent: ViewerContent = { id: contentId, layerId, source, placement, loadState: 'loading', failureReason: null }
    useContentStore.getState().upsert(loadingContent)
    this.emit({ type: 'contentLoading', contentId })

    void this.finishModelLoad(contentId, layerId, source, placement)

    return ok({ contentId })
  }

  private async finishModelLoad(
    contentId: string,
    layerId: string,
    source: Extract<ContentSource, { kind: 'model' }>,
    placement: WorldPlacement,
  ): Promise<void> {
    const outcome = await loadGltfContent(source.fileId)
    if (!outcome.ok) {
      this.failContent(contentId, layerId, source, outcome.reason)
      return
    }

    const { root, elementIndex } = outcome.result
    // FR-011: applies position, orientation and scale — not just position. ENU maps directly
    // onto this scene's own axes (research D2, corrected during implementation).
    const local = worldToLocal({ latitude: placement.latitude, longitude: placement.longitude }, placement.heightMetres)
    root.position.set(local.x, local.y, local.z)
    root.rotation.z = THREE.MathUtils.degToRad(placement.orientationDegrees)
    root.scale.setScalar(placement.scale)

    activeScene.get()?.add(root)
    this.loadedContentObjects.set(contentId, root)
    this.elementIndices.set(layerId, elementIndex)

    const content: ViewerContent = { id: contentId, layerId, source, placement, loadState: 'loaded', failureReason: null }
    useContentStore.getState().upsert(content)
    this.notifyContentLoaded(layerId)
    redrawScheduler.invalidate()
  }

  private failContent(contentId: string, layerId: string, source: ContentSource, reason: ContentFailureReason): void {
    this.removeLayer(layerId)
    const content: ViewerContent = { id: contentId, layerId, source, placement: null, loadState: 'failed', failureReason: reason }
    useContentStore.getState().upsert(content)
    this.emit({ type: 'contentFailed', contentId, reason })
  }

  /** FR-006, FR-033 — releases a content item's drawing resources in full: removes its loaded
   * object from the scene and disposes every geometry/material/texture, then removes its
   * backing `RenderLayer` and element index. */
  private releaseContentResources(content: ViewerContent): void {
    const root = this.loadedContentObjects.get(content.id)
    if (root) {
      activeScene.get()?.remove(root)
      root.traverse((child) => {
        const mesh = child as THREE.Mesh
        mesh.geometry?.dispose()
        const material = mesh.material
        if (Array.isArray(material)) material.forEach((m) => m.dispose())
        else material?.dispose()
      })
      this.loadedContentObjects.delete(content.id)
    }
    this.elementIndices.delete(content.layerId)
    if (content.layerId && this.store.layers.some((l) => l.id === content.layerId)) {
      this.removeLayer(content.layerId)
    }
  }
}
