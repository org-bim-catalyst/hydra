import GppGoodOutlinedIcon from '@mui/icons-material/GppGoodOutlined'
import GppMaybeOutlinedIcon from '@mui/icons-material/GppMaybeOutlined'
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined'
import type { SvgIconComponent } from '@mui/icons-material'
import { Box, Tooltip, Typography, useTheme } from '@mui/material'
import { HudCard } from '../../../components/workspace-shell/HudCard'
import { useActiveLocationStore } from '../../../store/activeLocationStore'
import { useActiveSiteBoundaryStore, type SiteBoundaryConfidenceLevel } from '../../../store/activeSiteBoundaryStore'

const CONFIDENCE_LABEL: Record<SiteBoundaryConfidenceLevel, string> = {
  high: 'High confidence',
  medium: 'Medium confidence',
  low: 'Low confidence — approximate',
}

/** specs/073 data-model "confidence → visual" (FR-008–FR-011). Confidence is carried three ways
 * at once — the shield's shape, its colour, and the level in words (the card's accessible name and
 * the shield's tooltip) — so colour is never the only signal (WCAG 2.1 AA 1.4.1). The tone names a
 * theme palette entry rather than a hex value, so the colour follows the light/dark theme like the
 * rest of the card. */
const CONFIDENCE_VISUAL: Record<SiteBoundaryConfidenceLevel, { Icon: SvgIconComponent; tone: 'success' | 'warning' | 'error' }> = {
  high: { Icon: GppGoodOutlinedIcon, tone: 'success' },
  medium: { Icon: ShieldOutlinedIcon, tone: 'warning' },
  low: { Icon: GppMaybeOutlinedIcon, tone: 'error' },
}

/** specs/042-site-boundary-resolution FR-004/FR-005/FR-006 — a compact, glanceable readout of the
 * site Lucy confirmed and its confidence level.
 *
 * It appears the moment the place is confirmed, from the location's own level, rather than waiting
 * for the outline: resolving a boundary takes seconds (Overpass, sometimes retried), and can fail
 * or be cut short outright, while Lucy has already said the place is confirmed and how surely
 * (found live 2026-09-25). Once the boundary lands its level replaces the location's — it is the
 * more specific judgement, of the area actually drawn. Renders nothing while neither is active,
 * including for a device-location fix, which Lucy never confirmed.
 *
 * specs/073: the last item of the studio's top-left HUD row (contributed as a `hudItem` by
 * `boundaryConfidenceExtension`), on the same 40 px `HudCard` surface as the weather card and the
 * project title. The card itself is just the shield and the site name, in bold like the project
 * title (the name truncates when space runs out); the level and the reason for it are in the
 * shield's tooltip — the outline's source once a boundary is drawn, the geocoder's precision
 * before that. The alternative candidates are not shown. The shield is the one part of the card
 * that takes the pointer (the rest lets drags through to the map) and it is focusable, so the
 * tooltip reaches keyboard users too. It uses `palette.X.light` in dark mode (research D7):
 * `.main` is tuned for light backgrounds and falls under 3:1 against a dark card. */
export function SiteBoundaryConfidenceBadge() {
  const theme = useTheme()
  const boundarySiteName = useActiveSiteBoundaryStore((s) => s.siteName)
  const boundaryLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)
  const locationSource = useActiveLocationStore((s) => s.source)
  const locationName = useActiveLocationStore((s) => s.locationName)
  const locationLevel = useActiveLocationStore((s) => s.confidenceLevel)
  const locationReason = useActiveLocationStore((s) => s.confidenceReason)
  const boundarySourceDetail = useActiveSiteBoundaryStore((s) => s.sourceDetail)

  const hasBoundary = Boolean(boundarySiteName && boundaryLevel)
  const siteName = hasBoundary ? boundarySiteName : locationSource === 'agent' ? locationName : null
  const confidenceLevel = hasBoundary ? boundaryLevel : locationSource === 'agent' ? locationLevel : null
  const reason = hasBoundary ? (boundarySourceDetail ? `Outline: ${boundarySourceDetail}` : null) : locationReason

  if (!siteName || !confidenceLevel) return null

  const label = CONFIDENCE_LABEL[confidenceLevel]
  const { Icon, tone } = CONFIDENCE_VISUAL[confidenceLevel]
  const iconColour = theme.palette[tone][theme.palette.mode === 'dark' ? 'light' : 'main']

  return (
    <HudCard
      role="status"
      aria-label={`${siteName} ${hasBoundary ? 'boundary' : 'location'}: ${label}`}
      maxWidth={360}
      sx={{ gap: 1 }}
    >
      <Tooltip
        describeChild
        title={
          <>
            <Typography variant="subtitle2" component="div" sx={{ fontWeight: 600 }}>
              {label}
            </Typography>
            {reason && (
              <Typography variant="body2" component="div">
                {reason}
              </Typography>
            )}
          </>
        }
      >
        <Box
          component="span"
          role="img"
          aria-label={label}
          tabIndex={0}
          sx={{
            display: 'inline-flex',
            flexShrink: 0,
            borderRadius: '50%',
            cursor: 'help',
            pointerEvents: 'auto',
            '&:focus-visible': { outline: '2px solid currentColor', outlineOffset: 2 },
          }}
        >
          <Icon aria-hidden="true" sx={{ fontSize: 20, color: iconColour }} />
        </Box>
      </Tooltip>
      <Typography variant="subtitle2" component="div" noWrap sx={{ minWidth: 0, fontWeight: 600 }}>
        {siteName}
      </Typography>
    </HudCard>
  )
}
