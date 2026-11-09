import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it } from 'vitest'
import { useSpatialLayersStore, type SpatialLayerPayload } from '../../../store/spatialLayersStore'
import { SpatialLayerLegend } from './SpatialLayerLegend'

function markerLayer(layerId: string, label: string, count = 2): SpatialLayerPayload {
  return {
    layerId,
    sourceOperation: 'op-1',
    name: label,
    legendLabel: label,
    kind: 'markers',
    markers: Array.from({ length: count }, (_, index) => ({
      id: `${layerId}-${index}`,
      name: `Feature ${index}`,
      latitude: 23.5 + index * 0.01,
      longitude: 58.3,
    })),
    style: { color: '#4285F4', markerShape: 'simple-dot' },
    visible: true,
  }
}

function seedLayers(...layers: SpatialLayerPayload[]) {
  useSpatialLayersStore.getState().applyLayers('chat-1', 'analysis-1', layers, 'replace')
}

describe('SpatialLayerLegend', () => {
  beforeEach(() => {
    useSpatialLayersStore.getState().clearLayers()
  })

  it('renders nothing when no layer is active', () => {
    const { container } = render(<SpatialLayerLegend />)

    expect(container).toBeEmptyDOMElement()
  })

  it('renders one row per layer with its feature count', () => {
    seedLayers(markerLayer('schools', 'Schools', 3), markerLayer('hospitals', 'Hospitals', 1))

    render(<SpatialLayerLegend />)

    expect(screen.getByText('Schools')).toBeInTheDocument()
    expect(screen.getByText('Hospitals')).toBeInTheDocument()
    expect(screen.getByText('3')).toBeInTheDocument()
  })

  it('explains overlay operation and criteria in the layer legend', () => {
    const layer: SpatialLayerPayload = {
      ...markerLayer('suitable', 'Suitable locations'), kind: 'feature-query', markers: [], features: [],
      overlay: { role: 'result', operation: 'intersection', criteria: ['population', 'road access'], sources: ['parcels', 'roads'], scoreMeaning: '0–1 suitability', coverage: ['Parcels: Complete'] },
      coverage: [{ sourceId: 'land-parcels', displayName: 'Land Parcels', coverageStatus: 'Partial', explanation: '2 invalid features excluded' }],
    }
    seedLayers(layer)
    render(<SpatialLayerLegend />)

    expect(screen.getByText('result · intersection · population, road access · parcels, roads · 0–1 suitability')).toBeInTheDocument()
    expect(screen.getByText('Parcels: Complete')).toBeInTheDocument()
    expect(screen.getByText('Land Parcels: Partial · 2 invalid features excluded')).toBeInTheDocument()
  })

  it('hides a layer without removing it from the legend', async () => {
    seedLayers(markerLayer('schools', 'Schools'))
    render(<SpatialLayerLegend />)

    await userEvent.click(screen.getByRole('button', { name: 'Hide Schools' }))

    expect(useSpatialLayersStore.getState().layers[0].visible).toBe(false)
    expect(screen.getByText('Schools')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Show Schools' })).toBeInTheDocument()
  })

  it('removes one layer and leaves the rest', async () => {
    seedLayers(markerLayer('schools', 'Schools'), markerLayer('hospitals', 'Hospitals'))
    render(<SpatialLayerLegend />)

    await userEvent.click(screen.getByRole('button', { name: 'Remove Schools' }))

    expect(useSpatialLayersStore.getState().layers.map((layer) => layer.layerId)).toEqual(['hospitals'])
    expect(screen.queryByText('Schools')).not.toBeInTheDocument()
  })

  it('clears every layer', async () => {
    seedLayers(markerLayer('schools', 'Schools'), markerLayer('hospitals', 'Hospitals'))
    render(<SpatialLayerLegend />)

    await userEvent.click(screen.getByRole('button', { name: 'Clear all' }))

    expect(useSpatialLayersStore.getState().layers).toEqual([])
  })
})
