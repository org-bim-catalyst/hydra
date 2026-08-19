import DomainIcon from '@mui/icons-material/Domain'
import InsightsIcon from '@mui/icons-material/Insights'
import PlaceIcon from '@mui/icons-material/Place'
import SpeedIcon from '@mui/icons-material/Speed'
import TaskAltIcon from '@mui/icons-material/TaskAlt'
import { Box, Chip, Grid, Stack, Typography } from '@mui/material'
import type { FeatureBlock as FeatureBlockData } from '../content/copy'
import { flumeriaColor, flumeriaRadius } from '../theme/flumeriaPalette'

const ICONS = {
  pin: PlaceIcon,
  building: DomainIcon,
  gauge: SpeedIcon,
  insights: InsightsIcon,
  check: TaskAltIcon,
} as const

/**
 * One alternating image/text feature block (spec.md FR-002) — reused five times by
 * `FeatureBlocksSection` from `copy.featureBlocks`, matching the reference design's
 * repeated icon-badge + heading + body + tag-pills + image composition rather than five
 * near-duplicate component files (constitution §III DRY).
 */
export function FeatureBlock({ heading, body, tags, icon, image, imageSide }: FeatureBlockData) {
  const Icon = ICONS[icon]

  const textColumn = (
    <Grid size={{ xs: 12, md: 6 }} sx={{ display: 'flex', alignItems: 'center' }}>
      <Stack spacing={2.5}>
        <Box
          sx={{
            width: 48,
            height: 48,
            borderRadius: `${flumeriaRadius.button}px`,
            bgcolor: flumeriaColor.greenLight,
            color: flumeriaColor.green,
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
          aria-hidden="true"
        >
          <Icon fontSize="medium" />
        </Box>
        <Typography component="h3" variant="h4" sx={{ color: flumeriaColor.heading, fontWeight: 800 }}>
          {heading}
        </Typography>
        <Typography variant="body1" sx={{ color: flumeriaColor.body, lineHeight: 1.75 }}>
          {body}
        </Typography>
        <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', gap: 1 }}>
          {tags.map((tag) => (
            <Chip
              key={tag}
              label={tag}
              size="small"
              sx={{ bgcolor: flumeriaColor.greenLight, color: flumeriaColor.greenLightText, fontWeight: 600 }}
            />
          ))}
        </Stack>
      </Stack>
    </Grid>
  )

  const imageColumn = (
    <Grid size={{ xs: 12, md: 6 }}>
      <Box
        component="img"
        src={image}
        alt=""
        loading="lazy"
        sx={{
          width: '100%',
          height: '100%',
          minHeight: 260,
          borderRadius: `${flumeriaRadius.panel}px`,
          border: `1px solid ${flumeriaColor.border}`,
          objectFit: 'cover',
          display: 'block',
        }}
      />
    </Grid>
  )

  return (
    <Grid container spacing={{ xs: 4, md: 8 }} sx={{ alignItems: 'center' }}>
      {imageSide === 'left' ? (
        <>
          {imageColumn}
          {textColumn}
        </>
      ) : (
        <>
          {textColumn}
          {imageColumn}
        </>
      )}
    </Grid>
  )
}
