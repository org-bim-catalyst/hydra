import { apiFetch } from '../../../api/httpClient'

export interface CreateSiteAnalysisProjectLinkResult {
  userChatId: string
}

/** contracts/site-analysis-api.md — `POST /api/v1/site-analysis/project-links` (FR-024a). */
export const createSiteAnalysisProjectLink = (theDigitalCoreProjectId: string, siteName: string) =>
  apiFetch<CreateSiteAnalysisProjectLinkResult>('/site-analysis/project-links', {
    method: 'POST',
    body: JSON.stringify({ theDigitalCoreProjectId, siteName }),
  })
