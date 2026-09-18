import { useQuery } from '@tanstack/react-query'
import { useEffect, useRef } from 'react'
import { apiFetch } from '../../../api/httpClient'
import { useFloatingPanelStore } from '../../../viewer/panels/store/floatingPanelStore'
import type { SiteAnalysisDto } from '../api/siteAnalysisApi'

const getSiteAnalysesForChat = (userChatId: string) =>
  apiFetch<SiteAnalysisDto[]>(`/site-analyses?userChatId=${encodeURIComponent(userChatId)}`)

/**
 * contracts/site-analysis-api.md, tasks.md T048 — reopens every completed finding's panel when a
 * conversation is opened, so navigation or a reload does not lose them (FR-016-FR-018). The
 * stored content document is replayed exactly as delivered — this hook never recomposes it.
 *
 * Fetch failures surface through this query's own error state (constitution §2 VIII); a caller
 * rendering `isError` can offer a retry rather than the failure being silent.
 */
export function useSiteAnalysisRehydration(userChatId: string | null) {
  const openPanel = useFloatingPanelStore((state) => state.openPanel)
  const rehydratedChatIds = useRef(new Set<string>())

  const query = useQuery({
    queryKey: ['chats', userChatId, 'site-analyses'],
    queryFn: () => getSiteAnalysesForChat(userChatId!),
    enabled: userChatId !== null,
  })

  useEffect(() => {
    if (!userChatId || !query.data || rehydratedChatIds.current.has(userChatId)) {
      return
    }

    for (const analysis of query.data) {
      for (const result of analysis.results) {
        if (result.status !== 'Completed' || !result.content) {
          continue
        }

        openPanel({
          requestId: result.id,
          title: `${result.analysisType} — ${analysis.siteName}`,
          kind: 'content',
          content: result.content,
        })
      }
    }

    rehydratedChatIds.current.add(userChatId)
  }, [userChatId, query.data, openPanel])

  return { isError: query.isError, retry: () => void query.refetch() }
}
