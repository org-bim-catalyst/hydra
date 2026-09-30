import { create } from 'zustand'

export type SiteBoundaryConfidenceLevel = 'low' | 'medium' | 'high'
export type SiteBoundarySource = 'OsmBoundary' | 'GovernmentCadastral' | 'AiInterpretation' | 'UploadedBoundary' | 'ManualFallback' | 'RenderedMapExtraction' | 'UserCorrected'

export interface GeoPoint {
  latitude: number
  longitude: number
}

interface ActiveSiteBoundaryState {
  siteName: string | null
  /**
   * The conversation whose turn drew this outline. The viewer keeps it on screen across
   * conversations, but only that conversation's Lucy knows what it includes — so a new chat
   * asking for the same site must not show it while its own outline is still being resolved.
   */
  chatId: string | null
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
  /**
   * specs/079 — the token sent back as `expectedRevision` when saving an edit. Null until the chat
   * detail supplies it: a live `siteBoundary` event does not carry one, so the editor refetches first.
   */
  revision: string | null
  /** specs/079 — the outline is the user's own hand-edited one. */
  isHandEdited: boolean
}

interface ActiveSiteBoundaryActions {
  /** Replaces the active boundary wholesale — never a partial merge (a new site fully supersedes the previous one). */
  setBoundary(
    boundary: Omit<ActiveSiteBoundaryState, 'additionalPolygons' | 'chatId' | 'revision' | 'isHandEdited'> & {
      additionalPolygons?: GeoPoint[][]
      chatId?: string | null
      revision?: string | null
      isHandEdited?: boolean
    },
  ): void
  /** Edge case: the conversation switches to an entirely new, unrelated site — the previous boundary must disappear, not stay overlaid. */
  clearBoundary(): void
  /**
   * A conversation just confirmed `siteName`: keep the outline only if it is this site's and this
   * conversation drew it. Anything else is a previous site's, or this site as another chat left it
   * (a fresh chat showed Muscat Grand Mall with the Phase 2 an earlier chat had added, while its
   * own outline — the mall alone — was still being resolved). Cleared here rather than on the
   * next 'siteBoundary' event, which never comes when resolution is unavailable.
   */
  clearUnlessShowing(siteName: string, chatId: string): void
}

const emptyState: ActiveSiteBoundaryState = {
  siteName: null,
  chatId: null,
  centroid: null,
  polygon: null,
  additionalPolygons: [],
  areaSquareMeters: null,
  confidence: null,
  confidenceLevel: null,
  source: null,
  sourceDetail: null,
  alternativeCandidateNames: [],
  revision: null,
  isHandEdited: false,
}

/** Every ring of the active site — the main outline first — or none when there is no boundary. */
export const siteRingsOf = (state: Pick<ActiveSiteBoundaryState, 'polygon' | 'additionalPolygons'>): GeoPoint[][] =>
  state.polygon ? [state.polygon, ...state.additionalPolygons] : []

export const useActiveSiteBoundaryStore = create<ActiveSiteBoundaryState & ActiveSiteBoundaryActions>()((set, get) => ({
  ...emptyState,

  setBoundary(boundary) {
    // Absent means one outline: never let a previous site's extra rings stay behind.
    set({
      ...boundary,
      additionalPolygons: boundary.additionalPolygons ?? [],
      chatId: boundary.chatId ?? null,
      revision: boundary.revision ?? null,
      isHandEdited: boundary.isHandEdited ?? false,
    })
  },

  clearBoundary() {
    set({ ...emptyState })
  },

  clearUnlessShowing(siteName, chatId) {
    const { siteName: shownSite, chatId: shownFor } = get()
    if (shownSite !== siteName || shownFor !== chatId) {
      set({ ...emptyState })
    }
  },
}))
