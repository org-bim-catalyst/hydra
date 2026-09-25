import GppGoodOutlinedIcon from '@mui/icons-material/GppGoodOutlined'
import GppMaybeOutlinedIcon from '@mui/icons-material/GppMaybeOutlined'
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined'
import type { SvgIconComponent } from '@mui/icons-material'
import { Divider, Typography, useTheme } from '@mui/material'
import { HudCard } from '../../../components/workspace-shell/HudCard'
import { useActiveSiteBoundaryStore, type SiteBoundaryConfidenceLevel } from '../../../store/activeSiteBoundaryStore'

const CONFIDENCE_LABEL: Record<SiteBoundaryConfidenceLevel, string> = {
  high: 'High confidence',
  medium: 'Medium confidence',
  low: 'Low confidence — approximate',
}

/** specs/073 data-model "confidence → visual" (FR-008–FR-011). Confidence is carried three ways
 * at once — the shield's shape, its colour, and the text label — so no single one (colour least
 * of all) is the only signal (WCAG 2.1 AA 1.4.1). The tone names a theme palette entry rather
 * than a hex value, so the colour follows the light/dark theme like the rest of the card. */
const CONFIDENCE_VISUAL: Record<SiteBoundaryConfidenceLevel, { Icon: SvgIconComponent; tone: 'success' | 'warning' | 'error' }> = {
  high: { Icon: GppGoodOutlinedIcon, tone: 'success' },
  medium: { Icon: ShieldOutlinedIcon, tone: 'warning' },
  low: { Icon: GppMaybeOutlinedIcon, tone: 'error' },
}

/** specs/042-site-boundary-resolution FR-004/FR-005/FR-006 — a compact, glanceable readout of the
 * currently displayed site boundary and its confidence level. Renders nothing while no boundary is
 * active.
 *
 * specs/073: the last item of the studio's top-left HUD row (contributed as a `hudItem` by
 * `boundaryConfidenceExtension`), on the same 40 px `HudCard` surface as the weather card and the
 * project title. It shows only the confidence label and the site name — the data source and the
 * alternative candidates are no longer displayed. One line, read left to right like the weather
 * card beside it: shield, confidence label, a divider, then the site name (which truncates first
 * when space runs out). The shield uses `palette.X.light` in dark mode
 * (research D7): `.main` is tuned for light backgrounds and falls under 3:1 against a dark card. */
export function SiteBoundaryConfidenceBadge() {
  const theme = useTheme()
  const siteName = useActiveSiteBoundaryStore((s) => s.siteName)
  const confidenceLevel = useActiveSiteBoundaryStore((s) => s.confidenceLevel)

  if (!siteName || !confidenceLevel) return null

  const label = CONFIDENCE_LABEL[confidenceLevel]
  const { Icon, tone } = CONFIDENCE_VISUAL[confidenceLevel]
  const iconColour = theme.palette[tone][theme.palette.mode === 'dark' ? 'light' : 'main']

  return (
    <HudCard role="status" aria-label={`${siteName} boundary: ${label}`} maxWidth={360} sx={{ gap: 1 }}>
      <Icon aria-hidden="true" sx={{ fontSize: 20, flexShrink: 0, color: iconColour }} />
      <Typography variant="subtitle2" component="div" noWrap sx={{ flexShrink: 0, fontWeight: 600 }}>
        {label}
      </Typography>
      <Divider
        orientation="vertical"
        aria-hidden="true"
        sx={{ height: 20, alignSelf: 'center', borderColor: 'currentColor', opacity: 0.35 }}
      />
      <Typography variant="body2" component="div" noWrap sx={{ minWidth: 0, opacity: 0.85 }}>
        {siteName}
      </Typography>
    </HudCard>
  )
}
