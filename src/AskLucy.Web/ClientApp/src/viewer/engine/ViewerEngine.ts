import type { IViewerEngine } from '../api/engine'
import type { OverlayInput, RenderLayer, RenderLayerInput } from '../api/layers'
import type { CameraViewMode, ViewerCommandResult } from '../api/commands'
import type { ViewerEventHandler, ViewerEventType } from '../api/events'
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
}
