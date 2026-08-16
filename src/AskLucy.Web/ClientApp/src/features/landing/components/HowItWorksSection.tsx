import AnalyticsIcon from '@mui/icons-material/BarChart'
import GridViewIcon from '@mui/icons-material/GridView'
import BalanceIcon from '@mui/icons-material/Balance'
import SearchIcon from '@mui/icons-material/Search'
import { Box, Grid, Stack, Typography } from '@mui/material'
import { howItWorks } from '../content/copy'
import { flumeriaColor } from '../theme/flumeriaPalette'

const STEP_ICONS = {
  search: SearchIcon,
  analyze: AnalyticsIcon,
  evaluate: BalanceIcon,
  design: GridViewIcon,
} as const

/**
 * Four-step numbered workflow with a connecting line (spec.md FR-002 "how AI-assisted
 * urban design works" + "how Lucy interacts with the environment") — matches the reference
 * design's "How It Works" section structure exactly (Discover → Analyze → Evaluate →
 * Design), restyled in the Flumeria green palette.
 */
export function HowItWorksSection() {
  return (
    <Stack
      component="section"
      aria-labelledby="how-it-works-heading"
      spacing={{ xs: 6, md: 8 }}
      sx={{ px: { xs: 3, sm: 6, md: 10 }, py: { xs: 8, md: 12 }, bgcolor: flumeriaColor.offWhite, textAlign: 'center' }}
    >
      <Stack spacing={1.5} sx={{ alignItems: 'center' }}>
        <Typography variant="overline" sx={{ color: flumeriaColor.green, letterSpacing: '0.14em', fontWeight: 700 }}>
          {howItWorks.eyebrow}
        </Typography>
        <Typography id="how-it-works-heading" component="h2" variant="h3" sx={{ color: flumeriaColor.heading, fontWeight: 800 }}>
          {howItWorks.title}
        </Typography>
      </Stack>

      <Grid container spacing={{ xs: 5, md: 2 }} sx={{ position: 'relative' }}>
        {/* Connecting line behind the step circles, desktop only */}
        <Box
          aria-hidden="true"
          sx={{
            display: { xs: 'none', md: 'block' },
            position: 'absolute',
            top: 28,
            left: '6%',
            right: '6%',
            height: 2,
            bgcolor: flumeriaColor.green,
            opacity: 0.35,
          }}
        />
        {howItWorks.steps.map((step) => {
          const Icon = STEP_ICONS[step.icon]
          return (
            <Grid key={step.number} size={{ xs: 12, sm: 6, md: 3 }}>
              <Stack spacing={1.5} sx={{ alignItems: 'center' }}>
                <Box
                  sx={{
                    position: 'relative',
                    zIndex: 1,
                    width: 56,
                    height: 56,
                    borderRadius: '50%',
                    border: `2px solid ${flumeriaColor.green}`,
                    bgcolor: flumeriaColor.offWhite,
                    color: flumeriaColor.green,
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontWeight: 700,
                  }}
                >
                  {step.number}
                </Box>
                <Icon sx={{ color: flumeriaColor.green }} aria-hidden="true" />
                {/* component="p": these are step labels within one card, not document-
                    outline headings — variant="h6" would default to a real <h6> and skip
                    from this section's <h2> straight past h3-h5 (axe heading-order). */}
                <Typography variant="h6" component="p" sx={{ color: flumeriaColor.heading, fontWeight: 700 }}>
                  {step.title}
                </Typography>
                <Typography variant="body2" sx={{ color: flumeriaColor.body, maxWidth: 260 }}>
                  {step.body}
                </Typography>
              </Stack>
            </Grid>
          )
        })}
      </Grid>
    </Stack>
  )
}
