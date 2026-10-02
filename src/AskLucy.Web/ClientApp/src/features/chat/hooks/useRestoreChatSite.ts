import { useEffect } from 'react'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore, type SiteBoundarySource } from '../../../store/activeSiteBoundaryStore'
import { locationKeyOf, restoreRememberedCamera, setCameraMemoryChat } from '../../../viewer/session/chatCameraMemory'
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
    // The map's position is remembered per chat from here on (and put back below, once the site is on screen).
    setCameraMemoryChat(chatDetail?.id ?? null)

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
        location.confidenceReason,
      )

    const boundary = chatDetail.activeBoundary
    if (boundary) {
      useActiveSiteBoundaryStore.getState().setBoundary({
        siteName: boundary.siteName,
        chatId: chatDetail.id,
        centroid: boundary.centroid,
        polygon: boundary.polygon,
        additionalPolygons: boundary.additionalPolygons ?? [],
        areaSquareMeters: boundary.areaSquareMeters,
        confidence: boundary.confidence,
        confidenceLevel: boundary.confidenceLevel,
        source: boundary.source as SiteBoundarySource,
        sourceDetail: boundary.sourceDetail,
        // Not persisted with the boundary — only the live resolution knows them.
        alternativeCandidateNames: [],
        revision: boundary.revision,
        isHandEdited: boundary.isHandEdited,
      })
    } else {
      // Same rule as a live 'location' event: an outline of some other site must not stay overlaid.
      useActiveSiteBoundaryStore.getState().clearUnlessShowing(location.locationName, chatDetail.id)
    }

    // The site moved the map to its own framing; put the user's own view of this chat back over it.
    restoreRememberedCamera(chatDetail.id, locationKeyOf(location.latitude, location.longitude))
  }, [chatDetail, locationSource])
}
