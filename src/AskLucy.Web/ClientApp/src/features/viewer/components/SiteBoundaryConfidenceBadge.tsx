import { Box, Stack, Typography } from '@mui/material'
import { RiQuestionLine, RiShieldCheckLine, RiShieldLine } from '@remixicon/react'
import type { ReactNode } from 'react'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'
import { useActiveSiteBoundaryStore, type SiteBoundaryConfidenceLevel } from '../../../store/activeSiteBoundaryStore'

const CONFIDENCE_LABEL: Record<SiteBoundaryConfidenceLevel, string> = {
  high: 'High confidence',
  medium: 'Medium confidence',
  low: 'Low confidence — approximate',
}

/** FR-006/FR-004/FR-005 — confidence is distinguished by icon + label text, not color alone
 * (WCAG 2.1 AA). Mirrors color usage loosely, but the icon/label carry the actual meaning. */
function confidenceIcon(level: SiteBoundaryConfidenceLevel): ReactNode {
  switch (level) {
    case 'high':
      return <RiShieldCheckLine size={20} aria-hidden="true" />
    case 'medium':
      return <RiShieldLine size={20} aria-hidden="true" />
    case 'low':
      return <RiQuestionLine size={20} aria-hidden="true" />
  }
}

/** specs/042-site-boundary-resolution FR-004/FR-005/FR-006 — a compact, glanceable readout of
 * the currently displayed site boundary's confidence level and data source, styled to match
 * `LocationWeatherWidget`'s dark-glass chrome. Renders nothing while no boundary is active. */
export function SiteBoundaryConfidenceBadge() {
  const siteName = useActiveSiteBoundaryStore((s) => s.siteName)
  const confidenceLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)
  const sourceDetail = useActiveSiteBoundaryStore((s) => s.sourceDetail)
  const alternativeCandidateNames = useActiveSiteBoundaryStore((s) => s.alternativeCandidateNames)

  if (!siteName || !confidenceLevel) return null

  return (
    <Box
      role="status"
      aria-label={`${siteName} boundary: ${CONFIDENCE_LABEL[confidenceLevel]}, source: ${sourceDetail ?? 'unknown'}`}
      sx={{
        position: 'absolute',
        // Opposite corner from LocationWeatherWidget (top-left) to avoid overlapping it.
        top: { xs: 76, sm: 84 },
        right: { xs: 16, sm: 24 },
        maxWidth: 280,
        pointerEvents: 'none',
        borderRadius: 2,
        px: 2,
        py: 1.25,
        bgcolor: CIRCULAR_ACTION_CHROME.expandedBg,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        color: CIRCULAR_ACTION_CHROME.icon,
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
      }}
    >
      <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
        {confidenceIcon(confidenceLevel)}
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="subtitle2" component="div" sx={{ lineHeight: 1.2 }} noWrap>
            {siteName}
          </Typography>
          <Typography variant="caption" component="div" sx={{ opacity: 0.9, lineHeight: 1.3 }}>
            {CONFIDENCE_LABEL[confidenceLevel]}
          </Typography>
        </Box>
      </Stack>
      {sourceDetail && (
        <Typography variant="caption" component="div" sx={{ opacity: 0.75, mt: 0.5 }}>
          Source: {sourceDetail}
        </Typography>
      )}
      {alternativeCandidateNames.length > 0 && (
        <Typography variant="caption" component="div" sx={{ opacity: 0.75, mt: 0.5 }}>
          Also considered: {alternativeCandidateNames.join(', ')}
        </Typography>
      )}
    </Box>
  )
}
