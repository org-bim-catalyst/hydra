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
  setBoundary(boundary: Omit<ActiveSiteBoundaryState, never>): void
  /** Edge case: the conversation switches to an entirely new, unrelated site — the previous boundary must disappear, not stay overlaid. */
  clearBoundary(): void
}

const emptyState: ActiveSiteBoundaryState = {
  siteName: null,
  centroid: null,
  polygon: null,
  areaSquareMeters: null,
  confidence: null,
  confidenceLevel: null,
  source: null,
  sourceDetail: null,
  alternativeCandidateNames: [],
}

export const useActiveSiteBoundaryStore = create<ActiveSiteBoundaryState & ActiveSiteBoundaryActions>()((set) => ({
  ...emptyState,

  setBoundary(boundary) {
    set({ ...boundary })
  },

  clearBoundary() {
    set({ ...emptyState })
  },
}))
