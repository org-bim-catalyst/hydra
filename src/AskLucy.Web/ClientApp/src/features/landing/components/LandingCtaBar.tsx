import { Button, Stack } from '@mui/material'
import { useNavigate } from 'react-router'
import { useFunnelAnalytics } from '../../analytics/hooks/useFunnelAnalytics'
import { useAuthStore } from '../../../store/authStore'
import { flumeriaColor, flumeriaRadius } from '../theme/flumeriaPalette'

const HOW_IT_WORKS_ANCHOR_ID = 'how-it-works-heading'

/**
 * The hero's two primary actions, matching the reference design exactly: "Start
 * Designing →" (auth-aware — signed-out visitors go to sign-up, signed-in visitors go
 * straight to the workspace, same behavior FR-006 describes for "Try the Platform" — the
 * user asked for this exact reference label/behavior in place of a separate labeled
 * button) and "Explore Flumeria" (scrolls to the first section below the hero — not a
 * conversion action, so no funnel event). "Sign In" and "Create Account / Sign Up"
 * (FR-003) live in the nav bar instead of the hero — see `LandingHero`.
 */
export function LandingCtaBar() {
  const navigate = useNavigate()
  const { recordCtaClick } = useFunnelAnalytics()
  const accessToken = useAuthStore((s) => s.accessToken)

  const handleStartDesigning = () => {
    recordCtaClick('TryPlatform')
    navigate(accessToken ? '/studio' : '/register')
  }

  const handleExplore = () => {
    document.getElementById(HOW_IT_WORKS_ANCHOR_ID)?.scrollIntoView({ behavior: 'smooth', block: 'start' })
  }

  return (
    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2} sx={{ pt: 1, alignItems: { xs: 'stretch', sm: 'center' } }}>
      <Button
        variant="contained"
        onClick={handleStartDesigning}
        sx={{
          bgcolor: flumeriaColor.green,
          borderRadius: `${flumeriaRadius.button}px`,
          px: 2.5,
          py: 1,
          fontWeight: 600,
          '&:hover': { bgcolor: flumeriaColor.greenDark },
        }}
      >
        Start Designing →
      </Button>
      <Button
        variant="text"
        onClick={handleExplore}
        sx={{ color: flumeriaColor.white, fontWeight: 500, textDecoration: 'none', '&:hover': { textDecoration: 'underline' } }}
      >
        Explore Flumeria
      </Button>
    </Stack>
  )
}
