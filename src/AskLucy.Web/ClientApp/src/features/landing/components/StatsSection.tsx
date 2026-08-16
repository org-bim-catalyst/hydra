import { Grid, Stack, Typography } from '@mui/material'
import { stats } from '../content/copy'
import { flumeriaColor } from '../theme/flumeriaPalette'

/** Solid-green stats band, matching the reference design. */
export function StatsSection() {
  return (
    <Grid
      component="section"
      aria-label="Platform statistics"
      container
      sx={{ bgcolor: flumeriaColor.green, px: { xs: 3, sm: 6, md: 10 }, py: { xs: 6, md: 8 } }}
    >
      {stats.map((stat) => (
        <Grid key={stat.label} size={{ xs: 6, md: 3 }}>
          <Stack spacing={0.5} sx={{ alignItems: 'center', textAlign: 'center' }}>
            {/* component="p": a stat figure, not a document-outline heading. */}
            <Typography variant="h3" component="p" sx={{ color: flumeriaColor.white, fontWeight: 800 }}>
              {stat.value}
            </Typography>
            <Typography variant="body2" sx={{ color: 'rgba(255,255,255,0.85)' }}>
              {stat.label}
            </Typography>
          </Stack>
        </Grid>
      ))}
    </Grid>
  )
}
