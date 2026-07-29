import { useMemo } from 'react'
import { Box, Stack, Typography, useTheme } from '@mui/material'
import { scaleLinear } from 'd3'

interface StatusSplitChartProps {
  title: string
  primaryLabel: string
  primaryCount: number
  secondaryLabel: string
  secondaryCount: number
}

const BAR_HEIGHT = 28
const WIDTH = 280

/**
 * d3-computed two-segment proportional bar, shared by the active/locked (FR-004) and
 * confirmed/pending (FR-005) dashboard splits — same shape, different labels/counts.
 */
export function StatusSplitChart({
  title,
  primaryLabel,
  primaryCount,
  secondaryLabel,
  secondaryCount,
}: StatusSplitChartProps) {
  const theme = useTheme()
  const total = primaryCount + secondaryCount

  const { primaryWidth, secondaryWidth } = useMemo(() => {
    if (total === 0) {
      return { primaryWidth: 0, secondaryWidth: 0 }
    }
    const scale = scaleLinear().domain([0, total]).range([0, WIDTH])
    return { primaryWidth: scale(primaryCount), secondaryWidth: scale(secondaryCount) }
  }, [primaryCount, secondaryCount, total])

  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        {title}
      </Typography>
      {total === 0 ? (
        <Typography variant="body2" color="text.secondary">
          No registered users yet.
        </Typography>
      ) : (
        <>
          <svg
            viewBox={`0 0 ${WIDTH} ${BAR_HEIGHT}`}
            width="100%"
            height={BAR_HEIGHT}
            role="img"
            aria-label={`${title}: ${primaryLabel} ${primaryCount}, ${secondaryLabel} ${secondaryCount}`}
          >
            <rect x={0} y={0} width={primaryWidth} height={BAR_HEIGHT} fill={theme.palette.success.main} rx={4}>
              <title>
                {primaryLabel}: {primaryCount}
              </title>
            </rect>
            <rect
              x={primaryWidth}
              y={0}
              width={secondaryWidth}
              height={BAR_HEIGHT}
              fill={theme.palette.grey[400]}
              rx={4}
            >
              <title>
                {secondaryLabel}: {secondaryCount}
              </title>
            </rect>
          </svg>
          <Stack direction="row" spacing={2} sx={{ mt: 1 }}>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: theme.palette.success.main }} />
              <Typography variant="body2">
                {primaryLabel}: {primaryCount}
              </Typography>
            </Stack>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
              <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: theme.palette.grey[400] }} />
              <Typography variant="body2">
                {secondaryLabel}: {secondaryCount}
              </Typography>
            </Stack>
          </Stack>
        </>
      )}
    </Box>
  )
}
