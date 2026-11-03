import { beforeEach, describe, expect, it } from 'vitest'
import { usePoiLayersStore, type PoiLayer } from './poiLayersStore'

const schools: PoiLayer = {
  category: 'school',
  displayName: 'Schools',
  color: '#4285F4',
  markerShape: 'classic-pin',
  markers: [{ id: 'osm_node_1', name: 'School A', latitude: 23.5, longitude: 58.3 }],
}

const hospitals: PoiLayer = {
  category: 'hospital',
  displayName: 'Hospitals',
  color: '#EA4335',
  markerShape: 'classic-pin',
  markers: [{ id: 'osm_node_2', name: 'Hospital A', latitude: 23.6, longitude: 58.4 }],
}

describe('poiLayersStore', () => {
  beforeEach(() => {
    usePoiLayersStore.setState({ chatId: null, layers: [] })
  })

  it('keeps both layers when the mode is keep', () => {
    usePoiLayersStore.getState().applyLayer('chat-1', schools, 'replace')
    usePoiLayersStore.getState().applyLayer('chat-1', hospitals, 'keep')

    expect(usePoiLayersStore.getState().layers.map((l) => l.category)).toEqual(['school', 'hospital'])
  })

  it('replaces every layer when the mode is replace', () => {
    usePoiLayersStore.getState().applyLayer('chat-1', schools, 'replace')
    usePoiLayersStore.getState().applyLayer('chat-1', hospitals, 'replace')

    expect(usePoiLayersStore.getState().layers.map((l) => l.category)).toEqual(['hospital'])
  })

  it('replaces the same category in place instead of duplicating it', () => {
    usePoiLayersStore.getState().applyLayer('chat-1', schools, 'replace')
    usePoiLayersStore.getState().applyLayer('chat-1', { ...schools, markers: [] }, 'keep')

    expect(usePoiLayersStore.getState().layers).toHaveLength(1)
    expect(usePoiLayersStore.getState().layers[0].markers).toEqual([])
  })

  it('never mixes layers from a different chat', () => {
    usePoiLayersStore.getState().applyLayer('chat-1', schools, 'replace')
    usePoiLayersStore.getState().applyLayer('chat-2', hospitals, 'keep')

    expect(usePoiLayersStore.getState().layers.map((l) => l.category)).toEqual(['hospital'])
  })

  it('removeLayer drops only that category and clears the chat when empty', () => {
    usePoiLayersStore.getState().applyLayer('chat-1', schools, 'replace')
    usePoiLayersStore.getState().applyLayer('chat-1', hospitals, 'keep')

    usePoiLayersStore.getState().removeLayer('school')
    expect(usePoiLayersStore.getState().layers.map((l) => l.category)).toEqual(['hospital'])
    expect(usePoiLayersStore.getState().chatId).toBe('chat-1')

    usePoiLayersStore.getState().removeLayer('hospital')
    expect(usePoiLayersStore.getState().layers).toEqual([])
    expect(usePoiLayersStore.getState().chatId).toBeNull()
  })

  it('clearLayers removes every category', () => {
    usePoiLayersStore.getState().applyLayer('chat-1', schools, 'replace')
    usePoiLayersStore.getState().applyLayer('chat-1', hospitals, 'keep')

    usePoiLayersStore.getState().clearLayers()

    expect(usePoiLayersStore.getState().layers).toEqual([])
    expect(usePoiLayersStore.getState().chatId).toBeNull()
  })
})
