import { useMemo } from 'react'
import { Box, Stack, Typography, useTheme } from '@mui/material'
import { arc as d3Arc, pie as d3Pie } from 'd3'
import type { RoleCount } from '../api/adminApi'

interface RoleDistributionChartProps {
  data: RoleCount[]
}

const SIZE = 180
const RADIUS = SIZE / 2

/**
 * d3-computed donut chart of role distribution (FR-007). Same "d3 computes, React
 * renders" split as the other charts (research.md Topic 6).
 */
export function RoleDistributionChart({ data }: RoleDistributionChartProps) {
  const theme = useTheme()

  const colorByRole = (roleName: string) => {
    if (roleName === 'Super User') return theme.palette.secondary.main
    if (roleName === 'Administrator') return theme.palette.primary.main
    return theme.palette.grey[400]
  }

  const total = data.reduce((sum, d) => sum + d.userCount, 0)

  const slices = useMemo(() => {
    if (total === 0) {
      return []
    }

    const pieGenerator = d3Pie<RoleCount>()
      .value((d) => d.userCount)
      .sort(null);

    const arcGenerator = d3Arc<ReturnType<typeof pieGenerator>[number]>()
      .innerRadius(RADIUS * 0.55)
      .outerRadius(RADIUS - 2);

    return pieGenerator(data).map((slice) => ({
      key: slice.data.roleName,
      path: arcGenerator(slice) ?? '',
      color: colorByRole(slice.data.roleName),
      roleName: slice.data.roleName,
      count: slice.data.userCount,
    }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data, total])

  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        Role distribution
      </Typography>
      {total === 0 ? (
        <Typography variant="body2" color="text.secondary">
          No registered users yet.
        </Typography>
      ) : (
        <Stack direction="row" spacing={2} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <svg
            viewBox={`0 0 ${SIZE} ${SIZE}`}
            width={SIZE}
            height={SIZE}
            role="img"
            aria-label={`Role distribution: ${data.map((d) => `${d.roleName} ${d.userCount}`).join(', ')}`}
          >
            <g transform={`translate(${RADIUS}, ${RADIUS})`}>
              {slices.map((slice) => (
                <path key={slice.key} d={slice.path} fill={slice.color}>
                  <title>
                    {slice.roleName}: {slice.count}
                  </title>
                </path>
              ))}
            </g>
          </svg>
          <Stack spacing={0.5}>
            {data.map((d) => (
              <Stack key={d.roleName} direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: colorByRole(d.roleName) }} />
                <Typography variant="body2">
                  {d.roleName}: {d.userCount}
                </Typography>
              </Stack>
            ))}
          </Stack>
        </Stack>
      )}
    </Box>
  )
}
