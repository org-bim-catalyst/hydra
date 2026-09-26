import { create } from 'zustand'

export type SiteBoundaryConfidenceLevel = 'low' | 'medium' | 'high'
export type SiteBoundarySource = 'OsmBoundary' | 'GovernmentCadastral' | 'AiInterpretation' | 'UploadedBoundary' | 'ManualFallback'

export interface GeoPoint {
  latitude: number
  longitude: number
}

interface ActiveSiteBoundaryState {
  siteName: string | null
  centroid: GeoPoint | null
  /** Exterior ring, closed (first point repeats as last). Null when no active boundary. */
  polygon: GeoPoint[] | null
  /**
   * specs/077 — further rings of the same site that do not touch the main one: a building of the
   * same development across a street, or a station. Empty for a site that is one outline.
   */
  additionalPolygons: GeoPoint[][]
  areaSquareMeters: number | null
  confidence: number | null
  confidenceLevel: SiteBoundaryConfidenceLevel | null
  source: SiteBoundarySource | null
  sourceDetail: string | null
  /** FR-008 — other similarly-plausible candidates named alongside the rendered one. */
  alternativeCandidateNames: string[]
}

interface ActiveSiteBoundaryActions {
  /** Replaces the active boundary wholesale — never a partial merge (a new site fully supersedes the previous one). */
  setBoundary(
    boundary: Omit<ActiveSiteBoundaryState, 'additionalPolygons'> & { additionalPolygons?: GeoPoint[][] },
  ): void
  /** Edge case: the conversation switches to an entirely new, unrelated site — the previous boundary must disappear, not stay overlaid. */
  clearBoundary(): void
}

const emptyState: ActiveSiteBoundaryState = {
  siteName: null,
  centroid: null,
  polygon: null,
  additionalPolygons: [],
  areaSquareMeters: null,
  confidence: null,
  confidenceLevel: null,
  source: null,
  sourceDetail: null,
  alternativeCandidateNames: [],
}

/** Every ring of the active site — the main outline first — or none when there is no boundary. */
export const siteRingsOf = (state: Pick<ActiveSiteBoundaryState, 'polygon' | 'additionalPolygons'>): GeoPoint[][] =>
  state.polygon ? [state.polygon, ...state.additionalPolygons] : []

export const useActiveSiteBoundaryStore = create<ActiveSiteBoundaryState & ActiveSiteBoundaryActions>()((set) => ({
  ...emptyState,

  setBoundary(boundary) {
    // Absent means one outline: never let a previous site's extra rings stay behind.
    set({ ...boundary, additionalPolygons: boundary.additionalPolygons ?? [] })
  },

  clearBoundary() {
    set({ ...emptyState })
  },
}))
