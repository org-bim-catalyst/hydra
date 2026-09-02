import { alpha, Box, Stack, Typography, useTheme } from '@mui/material'
import { RiQuestionLine, RiShieldCheckLine, RiShieldLine } from '@remixicon/react'
import type { ReactNode } from 'react'
import { useActiveSiteBoundaryStore, type SiteBoundaryConfidenceLevel } from '../../../store/activeSiteBoundaryStore'

/** The requested brand accent — used identically in light/dark mode (a mid-tone violet reads fine as an icon/border accent against both a light and a near-black card, so no per-mode variant is needed for the accent itself; only the surrounding card surface adapts). */
const ACCENT = '#9C62DE'

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
 * the currently displayed site boundary's confidence level and data source. Stacked below
 * `LocationWeatherWidget` in the top-left corner (previously top-right, where it overlapped the
 * viewer's ribbon controls) — same `left` offset, positioned underneath it. Unlike the
 * always-dark-glass floating widgets, this card follows the app's light/dark theme (`theme.
 * palette.mode`) since it reads more like an inline data card than a map-chrome control; the
 * `#9C62DE` accent marks the icon and a left border stripe in both modes. Renders nothing while
 * no boundary is active. */
export function SiteBoundaryConfidenceBadge() {
  const theme = useTheme()
  const siteName = useActiveSiteBoundaryStore((s) => s.siteName)
  const confidenceLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)
  const sourceDetail = useActiveSiteBoundaryStore((s) => s.sourceDetail)
  const alternativeCandidateNames = useActiveSiteBoundaryStore((s) => s.alternativeCandidateNames)

  if (!siteName || !confidenceLevel) return null

  const isDark = theme.palette.mode === 'dark'

  return (
    <Box
      role="status"
      aria-label={`${siteName} boundary: ${CONFIDENCE_LABEL[confidenceLevel]}, source: ${sourceDetail ?? 'unknown'}`}
      sx={{
        position: 'absolute',
        // Stacked directly under LocationWeatherWidget (top: {76,84}, left: {16,24}) — same
        // left offset, enough top clearance to clear its tallest (isStale) state.
        top: { xs: 168, sm: 180 },
        left: { xs: 16, sm: 24 },
        maxWidth: 280,
        pointerEvents: 'none',
        borderRadius: 2,
        borderLeft: `3px solid ${ACCENT}`,
        px: 2,
        py: 1.25,
        bgcolor: isDark ? alpha(theme.palette.background.paper, 0.85) : alpha('#FFFFFF', 0.92),
        border: `1px solid ${alpha(ACCENT, isDark ? 0.35 : 0.25)}`,
        borderLeftWidth: 3,
        backdropFilter: 'blur(12px)',
        color: theme.palette.text.primary,
        boxShadow: isDark ? '0 2px 10px rgba(0,0,0,0.45)' : '0 2px 10px rgba(0,0,0,0.15)',
      }}
    >
      <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center', color: ACCENT }}>
        {confidenceIcon(confidenceLevel)}
        <Box sx={{ minWidth: 0, color: theme.palette.text.primary }}>
          <Typography variant="subtitle2" component="div" sx={{ lineHeight: 1.2 }} noWrap>
            {siteName}
          </Typography>
          <Typography variant="caption" component="div" sx={{ opacity: 0.8, lineHeight: 1.3 }}>
            {CONFIDENCE_LABEL[confidenceLevel]}
          </Typography>
        </Box>
      </Stack>
      {sourceDetail && (
        <Typography variant="caption" component="div" sx={{ opacity: 0.7, mt: 0.5 }}>
          Source: {sourceDetail}
        </Typography>
      )}
      {alternativeCandidateNames.length > 0 && (
        <Typography variant="caption" component="div" sx={{ opacity: 0.7, mt: 0.5 }}>
          Also considered: {alternativeCandidateNames.join(', ')}
        </Typography>
      )}
    </Box>
  )
}
