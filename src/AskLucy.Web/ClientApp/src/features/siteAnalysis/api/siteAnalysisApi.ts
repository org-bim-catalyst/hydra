import { apiFetch } from '../../../api/httpClient'

/** contracts/site-analysis-hub-events.md — mirrors `SiteAnalysisResultReceivedDto`. */
export interface SiteAnalysisResultReceivedPayload {
  analysisId: string
  resultId: string
  userChatId: string
  analysisType: string
  noticeText: string
  generatedAtUtc: string
}

/** contracts/site-analysis-hub-events.md — mirrors `SiteAnalysisCompletedDto`. `noticeText` is
 * `null` when every specialist succeeded (FR-024's quiet is preserved); both notices are also
 * persisted as real assistant messages server-side, so this payload exists only for immediate,
 * low-latency signalling — the messages query is the source of truth on reload. */
export interface SiteAnalysisCompletedPayload {
  analysisId: string
  userChatId: string
  status: 'Completed' | 'Failed'
  succeededCount: number
  expectedCount: number
  noticeText: string | null
}

/** contracts/site-analysis-api.md — enums serialize as strings API-wide; never compare numerically. */
export type SiteAnalysisStatus = 'Running' | 'Completed' | 'Failed'
export type SiteAnalysisResultStatus = 'Completed' | 'Failed' | 'Rejected'
export type SiteAnalysisConfidenceLevel = 'Low' | 'Medium' | 'High'

export interface SiteAnalysisResultDto {
  id: string
  analysisType: string
  status: SiteAnalysisResultStatus
  dataSource: string | null
  confidenceLevel: SiteAnalysisConfidenceLevel | null
  documentId: string | null
  content: { blocks: unknown[] } | null
  completedAtUtc: string
}

export interface SiteAnalysisDto {
  id: string
  userChatId: string
  siteName: string
  latitude: number
  longitude: number
  status: SiteAnalysisStatus
  expectedResultCount: number
  startedAtUtc: string
  completedAtUtc: string | null
  results: SiteAnalysisResultDto[]
}

/** GET /api/v1/site-analyses/{id} (contracts/site-analysis-api.md) — rehydration path (FR-017). A
 * 404 rejects this promise like any other failed request; callers surface it through TanStack
 * Query's error state, never swallowed. */
export const getSiteAnalysis = (id: string) => apiFetch<SiteAnalysisDto>(`/site-analyses/${id}`)
