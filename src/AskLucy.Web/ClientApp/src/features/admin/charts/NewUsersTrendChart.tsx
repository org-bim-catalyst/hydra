import { useMemo } from 'react'
import { Box, Typography, useTheme } from '@mui/material'
import { max as d3Max, scaleBand, scaleLinear } from 'd3'
import { useT } from '../../../i18n/useT'
import type { DailyUserCount } from '../api/adminApi'

interface NewUsersTrendChartProps {
  data: DailyUserCount[]
}

const CHART_HEIGHT = 200
const MARGIN = { top: 12, right: 12, bottom: 24, left: 28 }

/**
 * d3-computed bar chart of daily new-user registrations over the trailing 30 days
 * (FR-003). d3 is used only for scale math (`d3-scale`/`d3-array`) — React owns every
 * DOM node via JSX, avoiding the classic React/d3 "who owns this element" conflict
 * (research.md Topic 6).
 */
export function NewUsersTrendChart({ data }: NewUsersTrendChartProps) {
  const theme = useTheme()
  const t = useT('admin.dashboard')

  const { bars, yTicks, width } = useMemo(() => {
    const width = 640
    const innerWidth = width - MARGIN.left - MARGIN.right
    const innerHeight = CHART_HEIGHT - MARGIN.top - MARGIN.bottom

    const xScale = scaleBand<string>()
      .domain(data.map((d) => d.date))
      .range([0, innerWidth])
      .padding(0.25)

    const maxCount = d3Max(data, (d) => d.newUsers) ?? 0
    const yDomainMax = maxCount === 0 ? 1 : maxCount
    const yScale = scaleLinear().domain([0, yDomainMax]).range([innerHeight, 0]).nice()

    const bars = data.map((d) => ({
      key: d.date,
      x: xScale(d.date) ?? 0,
      y: yScale(d.newUsers),
      width: xScale.bandwidth(),
      height: innerHeight - yScale(d.newUsers),
      value: d.newUsers,
      date: d.date,
    }))

    const yTicks = yScale.ticks(4).map((tick) => ({ value: tick, y: yScale(tick) }))

    return { bars, yTicks, width }
  }, [data])

  const isEmpty = data.every((d) => d.newUsers === 0)

  return (
    <Box>
      <Typography variant="subtitle2" sx={{ mb: 1 }}>
        {t('trend.title')}
      </Typography>
      {isEmpty && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          {t('trend.empty')}
        </Typography>
      )}
      <svg
        viewBox={`0 0 ${width} ${CHART_HEIGHT}`}
        width="100%"
        height={CHART_HEIGHT}
        role="img"
        // d3 geometry is left-to-right in every language; only the text around it is translated.
        direction="ltr"
        aria-label={t('trend.ariaLabel', { total: data.reduce((sum, d) => sum + d.newUsers, 0) })}
      >
        <g transform={`translate(${MARGIN.left}, ${MARGIN.top})`}>
          {yTicks.map((tick) => (
            <line
              key={tick.value}
              x1={0}
              x2={640 - MARGIN.left - MARGIN.right}
              y1={tick.y}
              y2={tick.y}
              stroke={theme.palette.divider}
              strokeDasharray="2,2"
            />
          ))}
          {bars.map((bar) => (
            <rect
              key={bar.key}
              x={bar.x}
              y={bar.y}
              width={bar.width}
              height={Math.max(bar.height, 0)}
              fill={theme.palette.primary.main}
              rx={2}
            >
              <title>{t('trend.barTitle', { date: bar.date, value: bar.value })}</title>
            </rect>
          ))}
          {yTicks.map((tick) => (
            <text
              key={`label-${tick.value}`}
              x={-8}
              y={tick.y}
              textAnchor="end"
              dominantBaseline="middle"
              fontSize={11}
              fill={theme.palette.text.secondary}
            >
              {tick.value}
            </text>
          ))}
        </g>
      </svg>
    </Box>
  )
}
