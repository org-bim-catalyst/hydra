import { Alert, CircularProgress, Stack, Typography } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { useNavigate, useSearchParams } from 'react-router'
import { useActiveConversationStore } from '../chat/activeConversationStore'
import { createSiteAnalysisProjectLink } from './api/siteAnalysisApi'

/**
 * specs/050-park-site-analysis-agent FR-024a — the entry point a Project-linked deep link from
 * TheDigitalCore lands on. Exchanges the `projectId`/`siteName` query params for a (new or
 * reused) conversation via `POST /api/v1/site-analysis/project-links`, then redirects straight
 * into the normal chat UI (`/studio`) with that conversation selected — no bespoke chat/viewer
 * UI of its own, matching how `ExternalLoginCompletePage` handles a similar one-shot
 * exchange-then-redirect flow.
 */
export function DeepLinkEntry() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const setActiveChatId = useActiveConversationStore((s) => s.setActiveChatId)
  const requested = useRef(false)
  const [hasError, setHasError] = useState(false)

  const projectId = searchParams.get('projectId')
  const siteName = searchParams.get('siteName')

  useEffect(() => {
    if (requested.current || !projectId || !siteName) return
    requested.current = true

    createSiteAnalysisProjectLink(projectId, siteName)
      .then((result) => {
        setActiveChatId(result.userChatId)
        navigate('/studio', { replace: true })
      })
      .catch(() => setHasError(true))
    // eslint-disable-next-line react-hooks/exhaustive-deps -- navigate/setActiveChatId are stable; re-running on their identity would defeat the one-shot `requested` guard.
  }, [projectId, siteName])

  return (
    <Stack spacing={2.5} sx={{ alignItems: 'center', justifyContent: 'center', minHeight: '100vh', p: 4 }}>
      {hasError || !projectId || !siteName ? (
        <Alert severity="error" sx={{ maxWidth: 480 }}>
          We couldn't open this site. The link may be invalid or have expired.
        </Alert>
      ) : (
        <>
          <CircularProgress size={32} />
          <Typography variant="body2" color="text.secondary">
            Opening your site…
          </Typography>
        </>
      )}
    </Stack>
  )
}
