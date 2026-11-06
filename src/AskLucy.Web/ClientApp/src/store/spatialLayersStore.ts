import { create } from 'zustand'

/** specs/094 — the kinds the server can send. Unknown kinds are ignored by the client. */
export type SpatialLayerKind = 'markers' | 'buffer' | 'area' | 'band'

export interface SpatialMarkerPoint {
  id: string
  name: string
  latitude: number
  longitude: number
}

export interface SpatialRingPoint {
  latitude: number
  longitude: number
}

export interface SpatialLayerStyle {
  color?: string | null
  markerShape?: string | null
  opacity?: number | null
  strokeWidth?: number | null
}

/** One layer exactly as the server sends it. */
export interface SpatialLayerPayload {
  layerId: string
  sourceOperation: string
  name: string
  legendLabel: string
  kind: SpatialLayerKind
  markers?: SpatialMarkerPoint[] | null
  ring?: SpatialRingPoint[] | null
  style?: SpatialLayerStyle | null
  visible: boolean
}

/** One active layer; `analysisId` is stamped on arrival from the event's top-level id. */
export interface SpatialLayer extends SpatialLayerPayload {
  analysisId: string
}

interface SpatialLayersState {
  /** The chat these layers belong to; layers from another chat replace rather than mix. */
  chatId: string | null
  /** The analysis currently owning the map, for "clear this analysis". */
  analysisId: string | null
  layers: SpatialLayer[]
  /**
   * Applies one `__SPATIAL_LAYERS__` event. `replace` clears every other analysis first;
   * `keep` draws beside them. Layers belonging to the same analysis are always replaced, so a
   * repeated search never stacks duplicate markers.
   */
  applyLayers: (
    chatId: string,
    analysisId: string,
    incoming: SpatialLayerPayload[],
    mode: 'keep' | 'replace',
  ) => void
  /** Hides or shows one layer without changing its data. */
  toggleLayer: (layerId: string) => void
  /** Removes one layer — the legend's close button. */
  removeLayer: (layerId: string) => void
  /** Removes every layer belonging to the current analysis. */
  clearAnalysis: () => void
  /** Removes every layer — the legend's "Clear all". */
  clearLayers: () => void
}

const validKinds: readonly SpatialLayerKind[] = ['markers', 'buffer', 'area', 'band']

function isRenderable(layer: SpatialLayerPayload): boolean {
  if (!layer?.layerId || !validKinds.includes(layer.kind)) return false
  if (layer.kind === 'markers') return (layer.markers?.length ?? 0) > 0
  return (layer.ring?.length ?? 0) >= 3
}

export const useSpatialLayersStore = create<SpatialLayersState>((set) => ({
  chatId: null,
  analysisId: null,
  layers: [],

  applyLayers: (chatId, analysisId, incoming, mode) =>
    set((state) => {
      // A different chat never inherits the previous one's layers.
      const base = state.chatId === chatId ? state.layers : []
      const renderable = incoming
        .filter(isRenderable)
        .map((layer) => ({ ...layer, analysisId }))

      // "replace" with nothing to draw means the user asked to clear the old layers and the new
      // search found nothing — the old ones must still go, or the map contradicts the reply.
      if (renderable.length === 0) {
        return mode === 'replace'
          ? { chatId, analysisId, layers: [] }
          : { chatId: state.chatId, analysisId: state.analysisId, layers: base }
      }

      const withoutThisAnalysis = base.filter((layer) => layer.analysisId !== analysisId)
      const next =
        mode === 'replace' ? renderable : [...withoutThisAnalysis, ...renderable]

      return { chatId, analysisId, layers: next }
    }),

  toggleLayer: (layerId) =>
    set((state) => ({
      layers: state.layers.map((layer) =>
        layer.layerId === layerId ? { ...layer, visible: !layer.visible } : layer,
      ),
    })),

  removeLayer: (layerId) =>
    set((state) => {
      const layers = state.layers.filter((layer) => layer.layerId !== layerId)
      return {
        layers,
        chatId: layers.length === 0 ? null : state.chatId,
        analysisId: layers.length === 0 ? null : state.analysisId,
      }
    }),

  clearAnalysis: () =>
    set((state) => {
      const layers = state.layers.filter((layer) => layer.analysisId !== state.analysisId)
      return {
        layers,
        chatId: layers.length === 0 ? null : state.chatId,
        analysisId: layers.length === 0 ? null : state.analysisId,
      }
    }),

  clearLayers: () => set({ chatId: null, analysisId: null, layers: [] }),
}))
