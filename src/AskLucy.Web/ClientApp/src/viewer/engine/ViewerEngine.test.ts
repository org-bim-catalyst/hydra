import { beforeEach, describe, expect, it, vi } from 'vitest'
import * as THREE from 'three'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { useContentStore } from '../content/contentStore'
import { sceneAnchor } from '../scene/SceneAnchor'
import { ViewerEngine } from './ViewerEngine'

const { loadGltfContentMock } = vi.hoisted(() => ({ loadGltfContentMock: vi.fn() }))
vi.mock('../content/loaders/gltfContentLoader', () => ({ loadGltfContent: loadGltfContentMock }))

const initialState = useViewerEngineStore.getState()
const initialContentState = useContentStore.getState()

describe('ViewerEngine', () => {
  let engine: ViewerEngine

  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
    useContentStore.setState(initialContentState, true)
    sceneAnchor.set({ latitude: 25.2048, longitude: 55.2708 })
    loadGltfContentMock.mockReset()
    engine = new ViewerEngine()
  })

  describe('addLayer / removeLayer / setLayerVisibility (US2, FR-021/FR-022)', () => {
    it('adds a layer with defaults applied and emits layerAdded', () => {
      const handler = vi.fn()
      engine.on('layerAdded', handler)

      const result = engine.addLayer({ id: 'gis-1', kind: 'gis' })

      expect(result).toEqual({ ok: true, data: { layerId: 'gis-1' } })
      expect(useViewerEngineStore.getState().layers).toEqual([
        { id: 'gis-1', kind: 'gis', visible: true, zIndex: 0, metadata: {} },
      ])
      expect(handler).toHaveBeenCalledWith({ type: 'layerAdded', layerId: 'gis-1', kind: 'gis' })
    })

    it('generates an id when none is supplied', () => {
      const result = engine.addLayer({ kind: 'model' })
      expect(result.ok).toBe(true)
      expect(result.data?.layerId).toMatch(/^model-/)
    })

    it('fails with a caller-visible error on a duplicate id, without touching state', () => {
      engine.addLayer({ id: 'gis-1', kind: 'gis' })
      const result = engine.addLayer({ id: 'gis-1', kind: 'overlay' })

      expect(result.ok).toBe(false)
      expect(result.error).toContain('gis-1')
      expect(useViewerEngineStore.getState().layers).toHaveLength(1)
    })

    it('removes a layer and emits layerRemoved', () => {
      engine.addLayer({ id: 'gis-1', kind: 'gis' })
      const handler = vi.fn()
      engine.on('layerRemoved', handler)

      const result = engine.removeLayer('gis-1')

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().layers).toEqual([])
      expect(handler).toHaveBeenCalledWith({ type: 'layerRemoved', layerId: 'gis-1' })
    })

    it('fails to remove an unknown layer id', () => {
      const result = engine.removeLayer('does-not-exist')
      expect(result.ok).toBe(false)
      expect(result.error).toContain('does-not-exist')
    })

    it('toggles a layer visibility flag', () => {
      engine.addLayer({ id: 'gis-1', kind: 'gis' })
      const result = engine.setLayerVisibility('gis-1', false)
      expect(result.ok).toBe(true)
      expect(useViewerEngineStore.getState().layers[0].visible).toBe(false)
    })

    it('fails to toggle visibility for an unknown layer id', () => {
      const result = engine.setLayerVisibility('does-not-exist', false)
      expect(result.ok).toBe(false)
    })
  })

  describe('zoomToLocation (US2, US3, FR-021/FR-022)', () => {
    it('succeeds and forwards to the active render target when one is registered', () => {
      const panTo = vi.fn()
      engine.registerRenderTarget({ panTo })

      const result = engine.zoomToLocation(51.5074, -0.1278, 12)

      expect(result).toEqual({ ok: true, data: undefined })
      expect(panTo).toHaveBeenCalledWith(51.5074, -0.1278, 12)
    })

    it('succeeds as a no-op when no render target is registered (placeholder active, FR-013/FR-017)', () => {
      const result = engine.zoomToLocation(51.5074, -0.1278)
      expect(result).toEqual({ ok: true, data: undefined })
    })

    it('fails on an out-of-range latitude', () => {
      const result = engine.zoomToLocation(120, 0)
      expect(result.ok).toBe(false)
    })

    it('fails on an out-of-range longitude', () => {
      const result = engine.zoomToLocation(0, 200)
      expect(result.ok).toBe(false)
    })

    it('a render target unregistered via its cleanup function no longer receives commands', () => {
      const panTo = vi.fn()
      const unregister = engine.registerRenderTarget({ panTo })
      unregister()

      engine.zoomToLocation(0, 0)

      expect(panTo).not.toHaveBeenCalled()
    })
  })

  describe('setViewMode / setRotationEnabled (US3, FR-013/FR-014/FR-021–FR-023)', () => {
    it('updates camera.mode and emits viewModeChanged even with no active render target (placeholder, FR-013 as revised)', () => {
      const handler = vi.fn()
      engine.on('viewModeChanged', handler)

      const result = engine.setViewMode('plan')

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().camera.mode).toBe('plan')
      expect(handler).toHaveBeenCalledWith({ type: 'viewModeChanged', mode: 'plan' })
    })

    it('forwards to the active render target when one is registered', () => {
      const applyViewMode = vi.fn()
      engine.registerRenderTarget({ applyViewMode })

      engine.setViewMode('plan')

      expect(applyViewMode).toHaveBeenCalledWith('plan')
    })

    it('updates camera.rotationEnabled and emits rotationChanged even with no active render target', () => {
      const handler = vi.fn()
      engine.on('rotationChanged', handler)

      const result = engine.setRotationEnabled(false)

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().camera.rotationEnabled).toBe(false)
      expect(handler).toHaveBeenCalledWith({ type: 'rotationChanged', enabled: false })
    })

    it('forwards rotation state to the active render target when one is registered', () => {
      const applyRotationEnabled = vi.fn()
      engine.registerRenderTarget({ applyRotationEnabled })

      engine.setRotationEnabled(false)

      expect(applyRotationEnabled).toHaveBeenCalledWith(false)
    })
  })

  describe('setMapStyle', () => {
    it('updates mapStyle and emits mapStyleChanged even with no active render target (placeholder)', () => {
      const handler = vi.fn()
      engine.on('mapStyleChanged', handler)

      const result = engine.setMapStyle('satellite')

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().mapStyle).toBe('satellite')
      expect(handler).toHaveBeenCalledWith({ type: 'mapStyleChanged', mapStyle: 'satellite' })
    })

    it('forwards to the active render target when one is registered', () => {
      const applyMapStyle = vi.fn()
      engine.registerRenderTarget({ applyMapStyle })

      engine.setMapStyle('hybrid')

      expect(applyMapStyle).toHaveBeenCalledWith('hybrid')
    })

    it('accepts buildings-only like any other MapStyleId (specs/048-buildings-only-map-style)', () => {
      const handler = vi.fn()
      const applyMapStyle = vi.fn()
      engine.on('mapStyleChanged', handler)
      engine.registerRenderTarget({ applyMapStyle })

      const result = engine.setMapStyle('buildings-only')

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().mapStyle).toBe('buildings-only')
      expect(applyMapStyle).toHaveBeenCalledWith('buildings-only')
      expect(handler).toHaveBeenCalledWith({ type: 'mapStyleChanged', mapStyle: 'buildings-only' })
    })
  })

  describe('select / clearSelection (US5, FR-018/FR-019/FR-021–FR-023)', () => {
    it('fails for an unregistered element, without changing selection state', () => {
      const result = engine.select('gis-1', 'marker')
      expect(result.ok).toBe(false)
      expect(useViewerEngineStore.getState().selection).toEqual({
        selectedLayerId: null,
        selectedElementId: null,
      })
    })

    it('selects a registered element and emits selectionChanged', () => {
      engine.registerSelectableElement('gis-1', 'marker')
      const handler = vi.fn()
      engine.on('selectionChanged', handler)

      const result = engine.select('gis-1', 'marker')

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().selection).toEqual({
        selectedLayerId: 'gis-1',
        selectedElementId: 'marker',
      })
      expect(handler).toHaveBeenCalledWith({ type: 'selectionChanged', layerId: 'gis-1', elementId: 'marker' })
    })

    it('selecting a different element replaces the previous selection', () => {
      engine.registerSelectableElement('gis-1', 'marker-a')
      engine.registerSelectableElement('gis-1', 'marker-b')
      engine.select('gis-1', 'marker-a')

      engine.select('gis-1', 'marker-b')

      expect(useViewerEngineStore.getState().selection.selectedElementId).toBe('marker-b')
    })

    it('clearSelection empties the selection and reports empty on query, never stale (US5-AC3)', () => {
      engine.registerSelectableElement('gis-1', 'marker')
      engine.select('gis-1', 'marker')

      const result = engine.clearSelection()

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().selection).toEqual({
        selectedLayerId: null,
        selectedElementId: null,
      })
    })

    it('unregistering the currently-selected element clears the selection', () => {
      const unregister = engine.registerSelectableElement('gis-1', 'marker')
      engine.select('gis-1', 'marker')

      unregister()

      expect(useViewerEngineStore.getState().selection).toEqual({
        selectedLayerId: null,
        selectedElementId: null,
      })
    })
  })

  describe('displayContent / createOverlay (US6, FR-020/FR-021–FR-023)', () => {
    it('displays content on an existing layer and emits contentLoaded', () => {
      engine.addLayer({ id: 'model-1', kind: 'model' })
      const handler = vi.fn()
      engine.on('contentLoaded', handler)

      const result = engine.displayContent('model-1', { geometry: 'placeholder' })

      expect(result).toEqual({ ok: true, data: undefined })
      expect(useViewerEngineStore.getState().layers[0].metadata.content).toEqual({ geometry: 'placeholder' })
      expect(handler).toHaveBeenCalledWith({ type: 'contentLoaded', layerId: 'model-1' })
    })

    it('fails to display content on an unknown layer', () => {
      expect(engine.displayContent('does-not-exist', {}).ok).toBe(false)
    })

    it('fails to display null/undefined content', () => {
      engine.addLayer({ id: 'model-1', kind: 'model' })
      expect(engine.displayContent('model-1', null).ok).toBe(false)
      expect(engine.displayContent('model-1', undefined).ok).toBe(false)
    })

    it('creates an overlay layer and returns its overlayId', () => {
      const handler = vi.fn()
      engine.on('layerAdded', handler)

      const result = engine.createOverlay({ id: 'overlay-1', metadata: { kind: 'heatmap' } })

      expect(result).toEqual({ ok: true, data: { overlayId: 'overlay-1' } })
      expect(useViewerEngineStore.getState().layers[0]).toMatchObject({ id: 'overlay-1', kind: 'overlay' })
      expect(handler).toHaveBeenCalledWith({ type: 'layerAdded', layerId: 'overlay-1', kind: 'overlay' })
    })

    it('fails to create an overlay with a duplicate id', () => {
      engine.createOverlay({ id: 'overlay-1' })
      const result = engine.createOverlay({ id: 'overlay-1' })
      expect(result.ok).toBe(false)
    })
  })

  describe('loadContent / replaceContent / unloadContent / listContent (specs/051 US1, T025)', () => {
    const placement = { latitude: 25.21, longitude: 55.28, heightMetres: 0, orientationDegrees: 90, scale: 2 }

    it('loads gis content synchronously and lists it', () => {
      const result = engine.loadContent({ kind: 'gis', provider: 'google-maps', center: { latitude: 25.2, longitude: 55.27 } })
      expect(result.ok).toBe(true)

      const { content } = engine.listContent().data!
      expect(content).toHaveLength(1)
      expect(content[0].loadState).toBe('loaded')
    })

    it('loads model content asynchronously, applying orientation and scale to the loaded root transform (FR-011)', async () => {
      const root = new THREE.Object3D()
      loadGltfContentMock.mockResolvedValue({ ok: true, result: { root, elementIndex: new Map() } })

      const result = engine.loadContent({ kind: 'model', format: 'gltf', fileId: 'file-1' }, placement)
      expect(result.ok).toBe(true)
      expect(engine.listContent().data!.content[0].loadState).toBe('loading')

      await vi.waitFor(() => {
        expect(engine.listContent().data!.content[0].loadState).toBe('loaded')
      })

      expect(root.rotation.z).toBeCloseTo(Math.PI / 2)
      expect(root.scale.x).toBe(2)
    })

    it('content with no placement resolves to failed/unplaceable rather than being placed arbitrarily (FR-013)', () => {
      const result = engine.loadContent({ kind: 'model', format: 'gltf', fileId: 'file-1' })
      expect(result.ok).toBe(false)

      const content = engine.listContent().data!.content[0]
      expect(content.loadState).toBe('failed')
      expect(content.failureReason).toBe('unplaceable')
      expect(loadGltfContentMock).not.toHaveBeenCalled()
    })

    it('replaceContent releases the previous content\'s RenderLayer before the new content begins loading', () => {
      const first = engine.loadContent({ kind: 'gis', provider: 'google-maps', center: { latitude: 25.2, longitude: 55.27 } })
      const { contentId } = first.data!
      const firstLayerId = engine.listContent().data!.content[0].layerId

      const replaceResult = engine.replaceContent(contentId, {
        kind: 'gis',
        provider: 'google-maps',
        center: { latitude: 30, longitude: 30 },
      })

      expect(replaceResult.ok).toBe(true)
      expect(useViewerEngineStore.getState().layers.some((l) => l.id === firstLayerId)).toBe(false)
      expect(engine.listContent().data!.content).toHaveLength(1)
    })

    it('T052 (US4 AC5, FR-006): replacing model content disposes the previous content\'s geometry/material in full', async () => {
      const firstGeometry = new THREE.BoxGeometry()
      const firstMaterial = new THREE.MeshBasicMaterial()
      const disposeGeometry = vi.spyOn(firstGeometry, 'dispose')
      const disposeMaterial = vi.spyOn(firstMaterial, 'dispose')
      const firstRoot = new THREE.Object3D()
      firstRoot.add(new THREE.Mesh(firstGeometry, firstMaterial))
      loadGltfContentMock.mockResolvedValueOnce({ ok: true, result: { root: firstRoot, elementIndex: new Map() } })

      const { contentId } = engine.loadContent({ kind: 'model', format: 'gltf', fileId: 'file-1' }, placement).data!
      await vi.waitFor(() => {
        expect(engine.listContent().data!.content[0].loadState).toBe('loaded')
      })

      loadGltfContentMock.mockResolvedValueOnce({ ok: true, result: { root: new THREE.Object3D(), elementIndex: new Map() } })
      engine.replaceContent(contentId, { kind: 'model', format: 'gltf', fileId: 'file-2' }, placement)

      expect(disposeGeometry).toHaveBeenCalledTimes(1)
      expect(disposeMaterial).toHaveBeenCalledTimes(1)
    })

    it('T055 (US5 AC4): a content failure leaves already-displayed content displayed and the viewer usable', () => {
      const good = engine.loadContent({ kind: 'gis', provider: 'google-maps', center: { latitude: 25.2, longitude: 55.27 } })
      expect(good.ok).toBe(true)

      const failed = engine.loadContent({ kind: 'model', format: 'gltf', fileId: 'file-1' }) // no placement -> unplaceable
      expect(failed.ok).toBe(false)

      // The good content is still there, and the engine itself is still fully operable.
      expect(engine.listContent().data!.content.some((c) => c.id === good.data!.contentId)).toBe(true)
      expect(engine.zoomToLocation(25.2, 55.27).ok).toBe(true)
    })

    it('replaceContent fails for an unknown content id', () => {
      const result = engine.replaceContent('does-not-exist', { kind: 'gis', provider: 'google-maps', center: { latitude: 0, longitude: 0 } })
      expect(result.ok).toBe(false)
    })

    it('unloadContent removes content and releases its resources; fails gracefully for a non-existent id', () => {
      const { contentId } = engine.loadContent({ kind: 'gis', provider: 'google-maps', center: { latitude: 25.2, longitude: 55.27 } }).data!

      expect(engine.unloadContent(contentId).ok).toBe(true)
      expect(engine.listContent().data!.content).toHaveLength(0)
      expect(engine.unloadContent('unknown-id').ok).toBe(false)
    })

    it('T042 (US3): selectAndFrame selects and re-frames a known element', () => {
      engine.registerSelectableElement('gis-1', 'marker-1')
      const zoomHandler = vi.fn()
      engine.on('selectionChanged', zoomHandler)

      const result = engine.selectAndFrame('gis-1', 'marker-1')

      expect(result.ok).toBe(true)
      expect(zoomHandler).toHaveBeenCalledWith({ type: 'selectionChanged', layerId: 'gis-1', elementId: 'marker-1' })
    })

    it('T042 (US3 AC4): selectAndFrame fails with a stated reason for an element that no longer exists, never a silent no-op', () => {
      const result = engine.selectAndFrame('gis-1', 'does-not-exist')

      expect(result.ok).toBe(false)
      expect(result.error).toBeTruthy()
    })

    it('listContent reflects every currently-tracked content item, each independently removable', () => {
      const a = engine.loadContent({ kind: 'gis', provider: 'google-maps', center: { latitude: 25.2, longitude: 55.27 } }).data!
      const b = engine.loadContent({ kind: 'gis', provider: 'google-maps', center: { latitude: 30, longitude: 30 } }).data!

      expect(engine.listContent().data!.content.map((c) => c.id)).toEqual([a.contentId, b.contentId])

      engine.unloadContent(a.contentId)
      expect(engine.listContent().data!.content.map((c) => c.id)).toEqual([b.contentId])
    })
  })
})
