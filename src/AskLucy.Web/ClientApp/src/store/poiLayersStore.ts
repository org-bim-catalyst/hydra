import { create } from 'zustand'

/** One marker on the map for a POI category. */
export interface PoiMarker {
  id: string
  name: string
  latitude: number
  longitude: number
}

/** One category's markers plus the look the server assigned to it. */
export interface PoiLayer {
  category: string
  displayName: string
  color: string
  markerShape: string
  markers: PoiMarker[]
}

interface PoiLayersState {
  /** The chat these layers belong to; layers from another chat replace rather than mix. */
  chatId: string | null
  layers: PoiLayer[]
  /**
   * Applies one layer from the `__POI_LAYERS__` event. `mode: 'replace'` clears every other
   * category first; `keep` draws beside them. Same-category arrivals always replace that
   * category in place, so a repeated search never stacks duplicate markers.
   */
  applyLayer: (chatId: string, layer: PoiLayer, mode: 'keep' | 'replace') => void
  /** Removes one category — the legend's close button. */
  removeLayer: (category: string) => void
  /** Removes every category — the legend's "Clear all". */
  clearLayers: () => void
}

export const usePoiLayersStore = create<PoiLayersState>((set) => ({
  chatId: null,
  layers: [],

  applyLayer: (chatId, layer, mode) =>
    set((state) => {
      // A different chat never inherits the previous one's markers.
      const base = state.chatId === chatId ? state.layers : []
      const withoutCategory = base.filter((l) => l.category !== layer.category)

      return {
        chatId,
        layers: mode === 'replace' ? [layer] : [...withoutCategory, layer],
      }
    }),

  removeLayer: (category) =>
    set((state) => {
      const layers = state.layers.filter((l) => l.category !== category)
      return { layers, chatId: layers.length === 0 ? null : state.chatId }
    }),

  clearLayers: () => set({ chatId: null, layers: [] }),
}))
