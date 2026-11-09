import { create } from 'zustand'

/** specs/094 — the kinds the server can send. Unknown kinds are ignored by the client. */
export type SpatialLayerKind = 'markers' | 'buffer' | 'area' | 'band' | 'feature-query' | 'density-surface'

export interface DensityCell {
  id: string
  ring: SpatialRingPoint[]
  value: number
  unit: string
  classificationIndex?: number | null
  classificationLabel?: string | null
  sourceCount?: number | null
}
export interface DensitySurface {
  method: string
  unit: string
  searchRadiusMeters?: number | null
  cellSizeMeters?: number | null
  classification: { method: string; breaks: number[]; labels: string[] }
  cells: DensityCell[]
}

/** specs/095 — attribute map keyed by field name. Values may be string, number, or null. */
export type FeatureAttributes = Record<string, string | number | boolean | null>

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
  overlay?: OverlayFeatureMetadata | null
}

export interface OverlayFeatureMetadata {
  eligibility: Record<string, boolean>
  criterionScores: Record<string, number>
  score?: number | null
  rank?: number | null
  provenance: string[]
}

export interface OverlayLayerMetadata {
  role: string
  operation: string
  criteria: string[]
  sources: string[]
  scoreMeaning?: string | null
  coverage?: string[] | null
}

export interface SpatialCoverage {
  sourceId: string
  displayName: string
  coverageStatus: string
  explanation?: string | null
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
  density?: DensitySurface | null
  overlay?: OverlayLayerMetadata | null
  coverage?: SpatialCoverage[] | null
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
  'density-surface',
]

/** Validate untrusted streamed layer JSON before it reaches the viewer. */
export function isValidSpatialLayerPayload(value: unknown): value is SpatialLayerPayload {
  if (!value || typeof value !== 'object') return false
  const layer = value as Partial<SpatialLayerPayload>
  if (typeof layer.layerId !== 'string' || !layer.layerId || typeof layer.sourceOperation !== 'string' ||
      typeof layer.name !== 'string' || typeof layer.legendLabel !== 'string' || typeof layer.visible !== 'boolean' ||
      !validKinds.includes(layer.kind as SpatialLayerKind)) return false
  if (layer.kind !== 'feature-query') return true
  if (!Array.isArray(layer.features)) return false
  return layer.features.every((feature) => {
    if (!feature || typeof feature.featureId !== 'string' || typeof feature.name !== 'string') return false
    const geometry = feature.geometry
    if (!geometry || !['point', 'polygon', 'linestring'].includes(geometry.kind) || !Array.isArray(geometry.points)) return false
    if (geometry.points.some((point) => !point || !Number.isFinite(point.latitude) || !Number.isFinite(point.longitude) || Math.abs(point.latitude) > 90 || Math.abs(point.longitude) > 180)) return false
    const minimum = geometry.kind === 'point' ? 1 : geometry.kind === 'linestring' ? 2 : 4
    if (geometry.points.length < minimum || !feature.attributes || typeof feature.attributes !== 'object') return false
    if (geometry.kind === 'point' && geometry.points.length !== 1) return false
    if (geometry.kind === 'polygon') {
      const first = geometry.points[0]
      const last = geometry.points[geometry.points.length - 1]
      if (first.latitude !== last.latitude || first.longitude !== last.longitude) return false
    }
    if (Object.values(feature.attributes).some((attribute) =>
      attribute !== null && typeof attribute !== 'string' && typeof attribute !== 'boolean' &&
      !(typeof attribute === 'number' && Number.isFinite(attribute)))) return false
    if (feature.overlay != null && (
      !feature.overlay.eligibility || Object.values(feature.overlay.eligibility).some((eligible) => typeof eligible !== 'boolean') ||
      !feature.overlay.criterionScores || !Array.isArray(feature.overlay.provenance) || feature.overlay.provenance.some((source) => typeof source !== 'string') ||
      Object.values(feature.overlay.criterionScores).some((score) => !Number.isFinite(score)) ||
      (feature.overlay.score != null && !Number.isFinite(feature.overlay.score)) ||
      (feature.overlay.rank != null && (!Number.isInteger(feature.overlay.rank) || feature.overlay.rank < 1))
    )) return false
    return true
  }) && (layer.overlay == null || (
    typeof layer.overlay.role === 'string' && typeof layer.overlay.operation === 'string' &&
    Array.isArray(layer.overlay.criteria) && layer.overlay.criteria.every((criterion) => typeof criterion === 'string') &&
    Array.isArray(layer.overlay.sources) && layer.overlay.sources.every((source) => typeof source === 'string')
    && (layer.overlay.coverage == null || (Array.isArray(layer.overlay.coverage) && layer.overlay.coverage.every((entry) => typeof entry === 'string')))
  )) && (layer.coverage == null || (Array.isArray(layer.coverage) && layer.coverage.every((source) =>
    source && typeof source.sourceId === 'string' && typeof source.displayName === 'string' && typeof source.coverageStatus === 'string' &&
    (source.explanation == null || typeof source.explanation === 'string'))))
}

function isRenderable(layer: SpatialLayerPayload): boolean {
  if (!isValidSpatialLayerPayload(layer)) return false
  if (layer.kind === 'feature-query') {
    // feature-query layers are renderable even when empty (streaming in progress)
    return true
  }
  if (layer.kind === 'density-surface') {
    return (layer.density?.cells ?? []).every((cell) =>
      Number.isFinite(cell.value) && cell.value >= 0 && cell.ring.length >= 3)
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
