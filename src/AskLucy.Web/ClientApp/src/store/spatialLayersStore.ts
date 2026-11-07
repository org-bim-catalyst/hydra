import { create } from 'zustand'

/** specs/094 — the kinds the server can send. Unknown kinds are ignored by the client. */
export type SpatialLayerKind = 'markers' | 'buffer' | 'area' | 'band' | 'feature-query'

/** specs/095 — attribute map keyed by field name. Values may be string, number, or null. */
export type FeatureAttributes = Record<string, string | number | null>

/** specs/095 — geometry kind for a query result feature. */
export type FeatureGeometryKind = 'point' | 'polygon' | 'linestring'

/** specs/095 — geometry for one feature returned by a spatial query. */
export interface FeatureGeometry {
  kind: FeatureGeometryKind
  points: Array<{ latitude: number; longitude: number }>
}

/** specs/095 — one geographic feature returned by a spatial query provider. */
export interface SpatialQueryFeature {
  featureId: string
  name: string
  geometry: FeatureGeometry
  attributes: FeatureAttributes
}

export interface SpatialMarkerPoint {
  id: string
  name: string
  latitude: number
  longitude: number
  /** specs/095 — attribute values keyed by dataset field name. */
  attributes?: FeatureAttributes | null
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
  /** specs/095 — full feature list for feature-query layers. */
  features?: SpatialQueryFeature[] | null
  /** specs/095 — zero-based batch index; present during streaming. */
  batchIndex?: number | null
  /** specs/095 — running total of features across all batches so far. */
  totalCount?: number | null
  /** specs/095 — advisory provider hint at the eventual total. */
  estimatedTotal?: number | null
  /** specs/095 — true when the last batch has been received. */
  isFinal?: boolean | null
  /** specs/095 — warning message set when query timed out or produced a large result. */
  warning?: string | null
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
  /**
   * specs/095 — applies one streaming batch for a feature-query layer.
   * - `batchIndex === 0`: creates or replaces the layer.
   * - `batchIndex > 0`: appends features to the existing layer and updates totals.
   * - `isFinal === true`: marks the layer as fully loaded.
   */
  applyBatch: (
    chatId: string,
    analysisId: string,
    layerId: string,
    batch: SpatialLayerPayload,
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

const validKinds: readonly SpatialLayerKind[] = [
  'markers',
  'buffer',
  'area',
  'band',
  'feature-query',
]

function isRenderable(layer: SpatialLayerPayload): boolean {
  if (!layer?.layerId || !validKinds.includes(layer.kind)) return false
  if (layer.kind === 'feature-query') {
    // feature-query layers are renderable even when empty (streaming in progress)
    return true
  }
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

  applyBatch: (chatId, analysisId, layerId, batch, mode) =>
    set((state) => {
      const base = state.chatId === chatId ? state.layers : []
      const isFirstBatch = (batch.batchIndex ?? 0) === 0

      if (isFirstBatch) {
        // Replace or create the layer on batch 0.
        const withoutThisAnalysis =
          mode === 'replace'
            ? base.filter((l) => l.analysisId !== analysisId)
            : base.filter((l) => l.layerId !== layerId)

        const newLayer: SpatialLayer = {
          ...batch,
          layerId,
          analysisId,
          visible: true,
        }
        return { chatId, analysisId, layers: [...withoutThisAnalysis, newLayer] }
      }

      // Subsequent batches: append features to the existing layer.
      const layers = state.layers.map((layer) => {
        if (layer.layerId !== layerId) return layer

        const existingFeatures = layer.features ?? []
        const newFeatures = batch.features ?? []

        return {
          ...layer,
          features: [...existingFeatures, ...newFeatures],
          markers: [
            ...(layer.markers ?? []),
            ...(batch.markers ?? []),
          ],
          totalCount: batch.totalCount ?? layer.totalCount,
          estimatedTotal: batch.estimatedTotal ?? layer.estimatedTotal,
          isFinal: batch.isFinal ?? layer.isFinal,
          warning: batch.warning ?? layer.warning,
          legendLabel: batch.legendLabel ?? layer.legendLabel,
        }
      })

      return { chatId, analysisId, layers }
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
