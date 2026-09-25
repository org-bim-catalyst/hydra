import { useEffect } from 'react'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore, type SiteBoundarySource } from '../../../store/activeSiteBoundaryStore'
import type { ChatDetail } from '../api/chatsApi'

/**
 * Puts the site a conversation last confirmed back on the viewer when the conversation is opened.
 *
 * The chat has recorded its active location and boundary since specs/037/042, but the client only
 * ever learned them from a live reply's stream — so a page reload, or a turn cut short before its
 * boundary arrived (found live 2026-09-25: a deploy's host restart), left the viewer on the device
 * location with no outline and no confidence card, while Lucy's own context still held the site.
 *
 * Only while the viewer has no agent-confirmed site of its own: a site Lucy already put on screen
 * this session stays put when the user switches conversations, the same way the rest of the
 * workspace survives navigation.
 */
export function useRestoreChatSite(chatDetail: ChatDetail | undefined) {
  const locationSource = useActiveLocationStore((s) => s.source)

  useEffect(() => {
    const location = chatDetail?.activeLocation
    if (!location || locationSource === 'agent') return

    useActiveLocationStore
      .getState()
      .setFromAgent(
        location.latitude,
        location.longitude,
        location.locationName,
        location.confidence,
        null,
        null,
        location.confidenceLevel,
      )

    const boundary = chatDetail.activeBoundary
    if (boundary) {
      useActiveSiteBoundaryStore.getState().setBoundary({
        siteName: boundary.siteName,
        centroid: boundary.centroid,
        polygon: boundary.polygon,
        areaSquareMeters: boundary.areaSquareMeters,
        confidence: boundary.confidence,
        confidenceLevel: boundary.confidenceLevel,
        source: boundary.source as SiteBoundarySource,
        sourceDetail: boundary.sourceDetail,
        // Not persisted with the boundary — only the live resolution knows them.
        alternativeCandidateNames: [],
      })
    } else if (useActiveSiteBoundaryStore.getState().siteName !== location.locationName) {
      // Same rule as a live 'location' event: an outline of some other site must not stay overlaid.
      useActiveSiteBoundaryStore.getState().clearBoundary()
    }
  }, [chatDetail, locationSource])
}
