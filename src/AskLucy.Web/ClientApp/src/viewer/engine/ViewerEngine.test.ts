import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useViewerEngineStore } from '../store/viewerEngineStore'
import { ViewerEngine } from './ViewerEngine'

const initialState = useViewerEngineStore.getState()

describe('ViewerEngine', () => {
  let engine: ViewerEngine

  beforeEach(() => {
    useViewerEngineStore.setState(initialState, true)
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
})
