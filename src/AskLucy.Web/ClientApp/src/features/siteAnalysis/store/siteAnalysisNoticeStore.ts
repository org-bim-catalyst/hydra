import { create } from 'zustand'

export interface SiteAnalysisNotice {
  /** Stable per notice, so a notice delivered twice (a hub reconnect replaying it) is appended once. */
  id: string
  userChatId: string
  text: string
}

interface SiteAnalysisNoticeState {
  notices: SiteAnalysisNotice[]
  push: (notice: SiteAnalysisNotice) => void
  consume: (ids: string[]) => void
}

/**
 * specs/057-site-analysis-agent — hands a site-analysis chat notice from the hub (mounted in the
 * viewer's panels overlay) to whichever conversation view is showing that chat.
 *
 * Invalidating the messages query alone cannot do this: `useChatStream` deliberately stops
 * re-seeding from refetched history once the user has sent anything in the view, so an in-flight
 * stream is never overwritten — which is precisely the state every analysis notice arrives in.
 * The notice is already persisted server-side, so this store only covers the live view; a reload
 * shows it from history.
 */
export const useSiteAnalysisNoticeStore = create<SiteAnalysisNoticeState>()((set) => ({
  notices: [],
  push: (notice) =>
    set((state) =>
      state.notices.some((n) => n.id === notice.id) ? state : { notices: [...state.notices, notice] },
    ),
  consume: (ids) => set((state) => ({ notices: state.notices.filter((n) => !ids.includes(n.id)) })),
}))
