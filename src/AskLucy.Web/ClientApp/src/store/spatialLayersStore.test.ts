import { beforeEach, describe, expect, it } from 'vitest'
import { isValidSpatialLayerPayload, useSpatialLayersStore, type SpatialLayerPayload } from './spatialLayersStore'

function markerLayer(layerId: string, name = 'Schools'): SpatialLayerPayload {
  return {
    layerId,
    sourceOperation: 'op-1',
    name,
    legendLabel: name,
    kind: 'markers',
    markers: [{ id: `${layerId}-1`, name: 'A', latitude: 23.5, longitude: 58.3 }],
    style: { color: '#4285F4', markerShape: 'simple-dot' },
    visible: true,
  }
}

function areaLayer(layerId: string): SpatialLayerPayload {
  return {
    layerId,
    sourceOperation: 'op-2',
    name: 'Within 1 km',
    legendLabel: 'Within 1 km',
    kind: 'buffer',
    ring: [
      { latitude: 23.5, longitude: 58.3 },
      { latitude: 23.6, longitude: 58.3 },
      { latitude: 23.6, longitude: 58.4 },
    ],
    style: { color: '#1976D2', opacity: 0.25 },
    visible: true,
  }
}

describe('spatialLayersStore', () => {
  beforeEach(() => {
    useSpatialLayersStore.getState().clearLayers()
  })

  it('accepts complete overlay feature metadata and rejects invalid coordinates or scores', () => {
    const overlayLayer: SpatialLayerPayload = {
      layerId: 'overlay-result', sourceOperation: 'op-overlay', name: 'Suitable parcels',
      legendLabel: 'Suitable parcels', kind: 'feature-query', visible: true,
      features: [{
        featureId: 'parcel-1', name: 'Parcel 1', geometry: { kind: 'polygon', points: [
          { latitude: 1, longitude: 1 }, { latitude: 1, longitude: 2 },
          { latitude: 2, longitude: 2 }, { latitude: 1, longitude: 1 },
        ] }, attributes: { available: true }, overlay: {
          eligibility: { density: true }, criterionScores: { access: 0.8 },
          score: 0.8, rank: 1, provenance: ['parcels', 'roads'],
        },
      }],
      overlay: { role: 'result', operation: 'intersection', criteria: ['density', 'access'], sources: ['parcels', 'roads'] },
    }
    expect(isValidSpatialLayerPayload(overlayLayer)).toBe(true)
    expect(isValidSpatialLayerPayload({ ...overlayLayer, features: [{ ...overlayLayer.features![0], overlay: { ...overlayLayer.features![0].overlay!, score: Number.NaN } }] })).toBe(false)
  })

  it('replaces every layer when replace arrives', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools')], 'replace')
    store.applyLayers('chat-1', 'analysis-2', [areaLayer('buffer')], 'replace')

    const layers = useSpatialLayersStore.getState().layers
    expect(layers.map((layer) => layer.layerId)).toEqual(['buffer'])
    expect(layers[0].analysisId).toBe('analysis-2')
  })

  it('keeps other analyses when keep arrives and replaces its own layers', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools')], 'replace')
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools'), markerLayer('hospitals')], 'keep')

    expect(useSpatialLayersStore.getState().layers.map((layer) => layer.layerId)).toEqual([
      'schools',
      'hospitals',
    ])
  })

  it('clears every layer when replace arrives with nothing to draw', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools')], 'replace')
    store.applyLayers('chat-1', 'analysis-2', [], 'replace')

    expect(useSpatialLayersStore.getState().layers).toEqual([])
  })

  it('ignores layers that cannot be rendered', () => {
    const store = useSpatialLayersStore.getState()
    const broken: SpatialLayerPayload = {
      ...markerLayer('broken'),
      markers: [],
    }
    store.applyLayers('chat-1', 'analysis-1', [broken], 'replace')

    expect(useSpatialLayersStore.getState().layers).toEqual([])
  })

  it('toggles visibility without changing the layer data', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools')], 'replace')

    useSpatialLayersStore.getState().toggleLayer('schools')

    const layer = useSpatialLayersStore.getState().layers[0]
    expect(layer.visible).toBe(false)
    expect(layer.markers).toHaveLength(1)
  })

  it('removes one layer and leaves the rest', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers(
      'chat-1',
      'analysis-1',
      [markerLayer('schools'), markerLayer('hospitals')],
      'replace',
    )

    useSpatialLayersStore.getState().removeLayer('schools')

    expect(useSpatialLayersStore.getState().layers.map((layer) => layer.layerId)).toEqual(['hospitals'])
  })

  it('clears only the current analysis', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools')], 'replace')
    store.applyLayers('chat-1', 'analysis-2', [areaLayer('buffer')], 'keep')

    useSpatialLayersStore.getState().clearAnalysis()

    expect(useSpatialLayersStore.getState().layers.map((layer) => layer.layerId)).toEqual(['schools'])
  })

  it('never inherits another chat’s layers', () => {
    const store = useSpatialLayersStore.getState()
    store.applyLayers('chat-1', 'analysis-1', [markerLayer('schools')], 'replace')
    store.applyLayers('chat-2', 'analysis-2', [areaLayer('buffer')], 'replace')

    expect(useSpatialLayersStore.getState().layers.map((layer) => layer.layerId)).toEqual(['buffer'])
  })
})
