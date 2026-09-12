import { Chip } from '@mui/material'
import { useContentStore } from '../contentStore'

/** FR-005 — shows that content is loading rather than facing an unexplained pause. Reuses the
 * same Chip treatment `ExtensionFailureNotice`/the panel hub's connection indicator already use
 * (research D9's posture: no new notification system for an ambient, occasional viewer-level
 * state). Renders nothing while nothing is loading. */
export function ContentLoadingIndicator() {
  const content = useContentStore((s) => s.content)
  const loadingCount = content.filter((c) => c.loadState === 'loading').length

  if (loadingCount === 0) return null

  return (
    <Chip
      label={loadingCount === 1 ? 'Loading content…' : `Loading ${loadingCount} items…`}
      size="small"
      variant="outlined"
      color="default"
      data-testid="content-loading-indicator"
      sx={{
        position: 'absolute',
        top: { xs: 16, sm: 24 },
        left: '50%',
        transform: 'translateX(-50%)',
        bgcolor: 'background.paper',
      }}
    />
  )
}
