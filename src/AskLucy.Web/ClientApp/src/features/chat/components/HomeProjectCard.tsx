import { RiHomeLine } from '@remixicon/react'
import { Box, IconButton, Typography } from '@mui/material'
import { useNavigate } from 'react-router'

/** Sampled directly from the readdy.ai reference's own top-left card
 * (`getComputedStyle`): `rounded-full bg-background-100/60 backdrop-blur-md
 * border border-background-300/50`, positioned `absolute top-5 left-5`. */
const CARD_BG = 'oklch(0.18 0.02 280 / 0.6)'
const CARD_BORDER = '1px solid oklch(0.34 0.02 280 / 0.5)'
const BUTTON_BG = 'oklch(0.25 0.02 280 / 0.8)'
const TEXT_COLOR = 'oklch(0.97 0.01 100)'

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
        bgcolor: CARD_BG,
        border: CARD_BORDER,
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
          color: TEXT_COLOR,
          bgcolor: BUTTON_BG,
          '&:hover': { bgcolor: 'oklch(0.30 0.02 280 / 0.9)' },
        }}
      >
        <RiHomeLine size={20} />
      </IconButton>
      <Typography variant="subtitle2" sx={{ color: TEXT_COLOR, fontWeight: 600 }}>
        Flumeria Studio
      </Typography>
    </Box>
  )
}
