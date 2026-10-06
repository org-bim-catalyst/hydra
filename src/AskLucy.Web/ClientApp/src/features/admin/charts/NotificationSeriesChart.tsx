import { useMemo } from 'react'
import { Box, Stack, Typography, useTheme } from '@mui/material'
import { max as d3Max, scaleBand, scaleLinear } from 'd3'
import type { NotificationStatistics } from '../api/adminNotificationsApi'

interface NotificationSeriesChartProps {
  series: NotificationStatistics['series']
  /** Hourly buckets show the hour; daily ones the date. */
  hourly: boolean
}

const WIDTH = 720
const HEIGHT = 220
const MARGIN = { top: 12, right: 12, bottom: 28, left: 32 }
const SERIES = ['created', 'sent', 'failed'] as const
const SERIES_LABEL: Record<(typeof SERIES)[number], string> = { created: 'Created', sent: 'Sent', failed: 'Failed' }

/**
 * Created, sent and failed per time bucket (specs/067 FR-057), as grouped bars. d3 does only the scale
 * math and React owns every node, as in the other admin charts. The chart is a picture; the numbers it
 * draws are also given in full to a screen reader through its label and the legend totals.
 */
export function NotificationSeriesChart({ series, hourly }: NotificationSeriesChartProps) {
  const theme = useTheme()
  const color = { created: theme.palette.primary.main, sent: theme.palette.success.main, failed: theme.palette.error.main }

  const { groups, yTicks, bandwidth } = useMemo(() => {
    const innerWidth = WIDTH - MARGIN.left - MARGIN.right
    const innerHeight = HEIGHT - MARGIN.top - MARGIN.bottom
    const x = scaleBand<string>().domain(series.map((s) => s.bucketStartUtc)).range([0, innerWidth]).padding(0.2)
    const inner = scaleBand<string>().domain([...SERIES]).range([0, x.bandwidth()]).padding(0.05)
    const top = d3Max(series, (s) => Math.max(s.created, s.sent, s.failed)) ?? 0
    const y = scaleLinear().domain([0, top === 0 ? 1 : top]).range([innerHeight, 0]).nice()

    const groups = series.map((s) => ({
      key: s.bucketStartUtc,
      x: x(s.bucketStartUtc) ?? 0,
      bars: SERIES.map((name) => ({ name, x: inner(name) ?? 0, y: y(s[name]), height: innerHeight - y(s[name]), value: s[name] })),
    }))
    return { groups, yTicks: y.ticks(4).map((value) => ({ value, y: y(value) })), bandwidth: inner.bandwidth() }
  }, [series])

  const totals = SERIES.map((name) => ({ name, total: series.reduce((sum, s) => sum + s[name], 0) }))
  const label = (iso: string) => {
    const date = new Date(iso)
    return hourly ? `${String(date.getUTCHours()).padStart(2, '0')}:00` : `${date.getUTCMonth() + 1}/${date.getUTCDate()}`
  }
  const every = Math.max(1, Math.ceil(groups.length / 12))

  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        {hourly ? 'Per hour' : 'Per day'}
      </Typography>
      {series.length === 0 ? (
        <Typography variant="body2" color="text.secondary">
          Nothing was created or sent in this period.
        </Typography>
      ) : (
        <svg
          viewBox={`0 0 ${WIDTH} ${HEIGHT}`}
          width="100%"
          height={HEIGHT}
          role="img"
          aria-label={`Notifications ${hourly ? 'per hour' : 'per day'}: ${totals.map((t) => `${t.total} ${SERIES_LABEL[t.name].toLowerCase()}`).join(', ')}`}
        >
          <g transform={`translate(${MARGIN.left}, ${MARGIN.top})`}>
            {yTicks.map((tick) => (
              <g key={tick.value}>
                <line x1={0} x2={WIDTH - MARGIN.left - MARGIN.right} y1={tick.y} y2={tick.y} stroke={theme.palette.divider} strokeDasharray="2,2" />
                <text x={-6} y={tick.y} textAnchor="end" dominantBaseline="middle" fontSize={10} fill={theme.palette.text.secondary}>
                  {tick.value}
                </text>
              </g>
            ))}
            {groups.map((group, index) => (
              <g key={group.key} transform={`translate(${group.x}, 0)`}>
                {group.bars.map((bar) => (
                  <rect key={bar.name} x={bar.x} y={bar.y} width={bandwidth} height={bar.height} fill={color[bar.name]} />
                ))}
                {index % every === 0 && (
                  <text x={(bandwidth * 3) / 2} y={HEIGHT - MARGIN.top - MARGIN.bottom + 16} textAnchor="middle" fontSize={10} fill={theme.palette.text.secondary}>
                    {label(group.key)}
                  </text>
                )}
              </g>
            ))}
          </g>
        </svg>
      )}
      <Stack direction="row" spacing={2} sx={{ mt: 1 }}>
        {totals.map((t) => (
          <Stack key={t.name} direction="row" spacing={0.75} sx={{ alignItems: 'center' }}>
            <Box aria-hidden="true" sx={{ width: 10, height: 10, bgcolor: color[t.name], borderRadius: 0.5 }} />
            <Typography variant="caption">{`${SERIES_LABEL[t.name]}: ${t.total}`}</Typography>
          </Stack>
        ))}
      </Stack>
    </Box>
  )
}
