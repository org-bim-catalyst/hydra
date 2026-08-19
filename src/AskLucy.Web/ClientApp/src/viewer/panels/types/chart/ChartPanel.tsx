import { useMemo } from 'react'
import { Box, Stack, Typography, useTheme } from '@mui/material'
import { max as d3Max, scaleBand, scaleLinear, scaleOrdinal, line as d3Line } from 'd3'
import { z } from 'zod'
import { panelTypeRegistry } from '../../registry'

/** contracts/panel-type-registry.md "chart" built-in type — covers the spec's "Charts",
 * "environmental analysis", and "urban design metrics" categories via one generic primitive. */
export const chartDataSchema = z.object({
  chartKind: z.enum(['bar', 'line']),
  labels: z.array(z.string()).optional(),
  series: z
    .array(z.object({ label: z.string(), values: z.array(z.number()).min(1) }))
    .min(1),
})

export type ChartData = z.infer<typeof chartDataSchema>

const CHART_HEIGHT = 220
const MARGIN = { top: 12, right: 12, bottom: 28, left: 32 }
const SERIES_COLOR_KEYS = ['primary', 'secondary', 'success', 'warning', 'error', 'info'] as const

function ChartPanelRenderer({ data }: { data: ChartData }) {
  const theme = useTheme()
  const colorFor = scaleOrdinal<number, string>()
    .domain(data.series.map((_, i) => i))
    .range(SERIES_COLOR_KEYS.map((key) => theme.palette[key].main))

  const { width, innerWidth, innerHeight, xScale, yScale, yTicks, labels } = useMemo(() => {
    const pointCount = Math.max(...data.series.map((s) => s.values.length))
    const labels = data.labels ?? Array.from({ length: pointCount }, (_, i) => String(i + 1))
    const width = 560
    const innerWidth = width - MARGIN.left - MARGIN.right
    const innerHeight = CHART_HEIGHT - MARGIN.top - MARGIN.bottom
    const xScale = scaleBand<string>().domain(labels).range([0, innerWidth]).padding(0.2)
    const maxValue = d3Max(data.series.flatMap((s) => s.values)) ?? 0
    const yScale = scaleLinear()
      .domain([0, maxValue === 0 ? 1 : maxValue])
      .range([innerHeight, 0])
      .nice()
    const yTicks = yScale.ticks(4).map((tick) => ({ value: tick, y: yScale(tick) }))
    return { width, innerWidth, innerHeight, xScale, yScale, yTicks, labels }
  }, [data])

  const seriesBandScale = scaleBand<number>()
    .domain(data.series.map((_, i) => i))
    .range([0, xScale.bandwidth()])
    .padding(0.1)

  return (
    <Box>
      <svg
        viewBox={`0 0 ${width} ${CHART_HEIGHT}`}
        width="100%"
        height={CHART_HEIGHT}
        role="img"
        aria-label={`${data.chartKind} chart with ${data.series.length} series across ${labels.length} categories`}
      >
        <g transform={`translate(${MARGIN.left}, ${MARGIN.top})`}>
          {yTicks.map((tick) => (
            <line
              key={tick.value}
              x1={0}
              x2={innerWidth}
              y1={tick.y}
              y2={tick.y}
              stroke={theme.palette.divider}
              strokeDasharray="2,2"
            />
          ))}
          {yTicks.map((tick) => (
            <text
              key={`label-${tick.value}`}
              x={-8}
              y={tick.y}
              textAnchor="end"
              dominantBaseline="middle"
              fontSize={10}
              fill={theme.palette.text.secondary}
            >
              {tick.value}
            </text>
          ))}
          {labels.map((label) => (
            <text
              key={label}
              x={(xScale(label) ?? 0) + xScale.bandwidth() / 2}
              y={innerHeight + 16}
              textAnchor="middle"
              fontSize={10}
              fill={theme.palette.text.secondary}
            >
              {label}
            </text>
          ))}
          {data.chartKind === 'bar'
            ? data.series.map((series, seriesIndex) =>
                series.values.map((value, i) => {
                  const label = labels[i]
                  const x = (xScale(label) ?? 0) + (seriesBandScale(seriesIndex) ?? 0)
                  const barWidth = seriesBandScale.bandwidth()
                  const y = yScale(value)
                  return (
                    <rect
                      key={`${series.label}-${label}`}
                      x={x}
                      y={y}
                      width={barWidth}
                      height={Math.max(innerHeight - y, 0)}
                      fill={colorFor(seriesIndex)}
                      rx={1}
                    >
                      <title>
                        {series.label} — {label}: {value}
                      </title>
                    </rect>
                  )
                }),
              )
            : data.series.map((series, seriesIndex) => {
                const path = d3Line<number>()
                  .x((_, i) => (xScale(labels[i]) ?? 0) + xScale.bandwidth() / 2)
                  .y((value) => yScale(value))(series.values)
                return (
                  <path
                    key={series.label}
                    d={path ?? undefined}
                    fill="none"
                    stroke={colorFor(seriesIndex)}
                    strokeWidth={2}
                  />
                )
              })}
        </g>
      </svg>
      {data.series.length > 1 && (
        <Stack direction="row" spacing={2} sx={{ mt: 1, flexWrap: 'wrap' }}>
          {data.series.map((series, i) => (
            <Stack key={series.label} direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
              <Box sx={{ width: 10, height: 10, borderRadius: '50%', bgcolor: colorFor(i) }} />
              <Typography variant="caption" color="text.secondary">
                {series.label}
              </Typography>
            </Stack>
          ))}
        </Stack>
      )}
    </Box>
  )
}

panelTypeRegistry.register({
  typeKey: 'chart',
  renderer: ChartPanelRenderer,
  schema: chartDataSchema,
  defaultSize: { width: 480, height: 360 },
  resizable: true,
})
