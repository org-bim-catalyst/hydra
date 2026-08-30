import { RiHomeLine } from '@remixicon/react'
import { Box, IconButton, Typography, alpha, darken, lighten } from '@mui/material'
import type { Theme } from '@mui/material'
import { useNavigate } from 'react-router'

/**
 * The readdy.ai reference's top-left card: `rounded-full bg-background-100/60 backdrop-blur-md
 * border border-background-300/50`, positioned `absolute top-5 left-5`.
 *
 * Those were previously frozen as the dark literals they resolved to, because the reference was
 * light-mode only at the time. Its ramps invert between modes, so the same three roles are
 * mapped onto the MUI palette here instead — otherwise this card stays dark while the theme
 * toggle changes everything around it.
 */
const cardBg = (t: Theme) => alpha(t.palette.background.paper, 0.6)
const cardBorder = (t: Theme) => `1px solid ${alpha(t.palette.divider, 0.5)}`
const buttonBg = (t: Theme) => alpha(t.palette.background.paper, 0.8)
const buttonHoverBg = (t: Theme) =>
  t.palette.mode === 'dark' ? lighten(t.palette.background.paper, 0.08) : darken(t.palette.background.paper, 0.05)

/** The readdy.ai reference's top-left "Home › destination" breadcrumb card — this
 * feature has no equivalent "project/location" entity yet, so it shows the workspace's
 * own name (matching FR-001's "Flumeria Studio" rename) instead of inventing one. Home
 * navigates to `/`, which — for an already-authenticated visitor — redirects straight
 * back into `/studio` (spec 023's `PublicOnlyRoute`), the same "always lands you back in
 * the workspace" behavior the reference's own Home affordance implies. */
export function HomeProjectCard() {
  const navigate = useNavigate()

  return (
    <Box
      sx={{
        position: 'absolute',
        top: { xs: 16, sm: 20 },
        left: { xs: 16, sm: 20 },
        display: 'flex',
        alignItems: 'center',
        gap: 1,
        pl: 0.75,
        pr: 2,
        py: 0.75,
        borderRadius: `999px`,
        bgcolor: cardBg,
        border: cardBorder,
        backdropFilter: 'blur(12px)',
        pointerEvents: 'auto',
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
      }}
    >
      <IconButton
        aria-label="Home"
        onClick={() => navigate('/')}
        size="small"
        sx={{
          color: 'text.primary',
          bgcolor: buttonBg,
          '&:hover': { bgcolor: buttonHoverBg },
        }}
      >
        <RiHomeLine size={20} />
      </IconButton>
      <Typography variant="subtitle2" sx={{ color: 'text.primary', fontWeight: 600 }}>
        Flumeria Studio
      </Typography>
    </Box>
  )
}
