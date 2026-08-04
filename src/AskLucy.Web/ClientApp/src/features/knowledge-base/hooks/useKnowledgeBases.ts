import { useQuery } from '@tanstack/react-query'
import * as knowledgeBasesApi from '../api/knowledgeBasesApi'

export const KNOWLEDGE_BASES_QUERY_KEY = ['knowledge-bases']
export const KNOWLEDGE_BASES_DASHBOARD_SUMMARY_QUERY_KEY = [...KNOWLEDGE_BASES_QUERY_KEY, 'dashboard-summary']

/** Dashboard summary statistics cards (FR-029) — server caches this per-user for 60s, so client-side staleness beyond that window is expected and acceptable. */
export function useKnowledgeBaseDashboardSummary() {
  return useQuery({
    queryKey: KNOWLEDGE_BASES_DASHBOARD_SUMMARY_QUERY_KEY,
    queryFn: () => knowledgeBasesApi.getKnowledgeBaseDashboardSummary(),
  })
}

export function useKnowledgeBase(id: string | null) {
  return useQuery({
    queryKey: [...KNOWLEDGE_BASES_QUERY_KEY, id],
    queryFn: () => knowledgeBasesApi.getKnowledgeBase(id!),
    enabled: id !== null,
  })
}
