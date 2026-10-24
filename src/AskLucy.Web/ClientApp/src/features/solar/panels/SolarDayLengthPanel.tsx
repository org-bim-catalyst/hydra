import { Box, Stack, Typography, alpha, useTheme } from '@mui/material'
import { useEffect, useMemo, useRef } from 'react'
import { compactLabelSx } from '../../../viewer/panels/chrome/compactStyles'
import { copy } from '../copy'
import { FALLBACK_TIME_ZONE } from '../solar/timeZone'
import { yearProfile, type DayPhase, type YearProfile } from '../solar/twilight'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

/** The five bands, in the order they appear across a day, with their labels. */
const PHASES: readonly { phase: DayPhase; label: string }[] = [
  { phase: 'night', label: copy.phaseNightLabel },
  { phase: 'astronomical', label: copy.phaseAstronomicalLabel },
  { phase: 'nautical', label: copy.phaseNauticalLabel },
  { phase: 'civil', label: copy.phaseCivilLabel },
  { phase: 'day', label: copy.phaseDayLabel },
]

/** How strongly each band is drawn over the panel's own background. Daylight is the accent colour
 * at full strength; each twilight phase is the same hue stepped down, so the year reads as one
 * gradient from day to night rather than as five unrelated colours (research D11). */
const PHASE_STRENGTH: Record<DayPhase, number> = {
  day: 1,
  civil: 0.55,
  nautical: 0.3,
  astronomical: 0.15,
  night: 0.07,
}

const CHART_HEIGHT_PX = 320
/** The gutter the hour labels sit in, left of the plot. */
const HOUR_AXIS_WIDTH_PX = 30
/** A label every two hours: every hour is 13 px of chart at this height, which reads as noise. */
const AXIS_LABEL_STEP_HOURS = 2
const AXIS_FONT = '9px ui-monospace, SFMono-Regular, Menlo, monospace'

function formatShortDate(localDate: string): string {
  const [year, month, day] = localDate.split('-').map(Number)
  return new Intl.DateTimeFormat('en-GB', { timeZone: 'UTC', day: 'numeric', month: 'short' }).format(
    new Date(Date.UTC(year, month - 1, day, 12, 0, 0)),
  )
}

function formatLocalMinute(minuteOfDay: number): string {
  const hour = Math.floor(minuteOfDay / 60) % 24
  return `${String(hour).padStart(2, '0')}:${String(minuteOfDay % 60).padStart(2, '0')}`
}

/**
 * The year's day-length chart (specs/085 US2).
 *
 * A live panel rather than content, for two reasons recorded in research D7: the figures panel's
 * document is rebuilt on every playback tick, and putting a year of per-day boundaries inside it
 * would re-validate and re-render them every frame; and adding a kind to the shared content
 * vocabulary would move a contract every panel in the product depends on.
 *
 * Read-only by construction: it holds no control, writes no store value, and has no pointer or
 * keyboard handler (FR-015). It reads the site and the selected date from the same store the
 * figures panel reads, so the two cannot disagree (FR-014).
 */
export function SolarDayLengthPanel(): React.JSX.Element | null {
  const theme = useTheme()
  const site = useSolarAnalysisStore((s) => s.site)
  const moment = useSolarAnalysisStore((s) => s.moment)
  const canvasRef = useRef<HTMLCanvasElement | null>(null)

  const year = moment ? Number(moment.localDate.slice(0, 4)) : null
  const timeZoneId = site?.timeZoneId ?? FALLBACK_TIME_ZONE

  // Computed once per (site, year) and cached inside the module; a time-of-day change never
  // rebuilds it (research D9), and the panel re-renders without redrawing the year.
  const profile: YearProfile | null = useMemo(
    () => (site && year !== null ? yearProfile(site.latitude, site.longitude, year, timeZoneId) : null),
    [site, year, timeZoneId],
  )

  const colors = useMemo(() => {
    const accent = theme.palette.warning.main
    return Object.fromEntries(
      PHASES.map(({ phase }) => [phase, alpha(accent, PHASE_STRENGTH[phase])]),
    ) as Record<DayPhase, string>
  }, [theme])

  const selectedDate = moment?.localDate ?? null

  useEffect(() => {
    const canvas = canvasRef.current
    if (!canvas || !profile) return
    const context = canvas.getContext('2d')
    // jsdom has no 2d context; the chart is an enhancement over the text figures, so an
    // environment that cannot draw it still gets the caption, the legend and the extremes.
    if (!context) return

    const gridColor = alpha(theme.palette.text.primary, 0.12)
    const labelColor = theme.palette.text.secondary

    const draw = (): void => {
      const { width, height } = canvas.getBoundingClientRect()
      if (width < 2 || height < 2) return

      const dpr = window.devicePixelRatio || 1
      canvas.width = Math.round(width * dpr)
      canvas.height = Math.round(height * dpr)
      context.setTransform(dpr, 0, 0, dpr, 0, 0)
      context.clearRect(0, 0, width, height)

      // The year runs left to right and the local day runs top to bottom, so the daylight region
      // reads as two curves — the year's sunrise and sunset times (FR-012).
      const plotLeft = HOUR_AXIS_WIDTH_PX
      const plotWidth = Math.max(width - plotLeft, 1)
      const columnWidth = plotWidth / profile.days.length

      profile.days.forEach((day, index) => {
        const spanMinutes = day.segments[day.segments.length - 1]?.endMinute || 1
        for (const segment of day.segments) {
          context.fillStyle = colors[segment.phase]
          context.fillRect(
            plotLeft + index * columnWidth,
            (segment.startMinute / spanMinutes) * height,
            // Half a pixel of overlap: at 365 columns the bands are sub-pixel and a gap between
            // them would read as a stripe of the panel's background. The last column stops at
            // the plot's edge rather than painting over the panel's border.
            index === profile.days.length - 1 ? columnWidth : columnWidth + 0.5,
            Math.max(((segment.endMinute - segment.startMinute) / spanMinutes) * height, 0.4) + 0.5,
          )
        }
      })

      // Hour gridlines and labels, drawn over the bands so the curves can be read as times.
      context.font = AXIS_FONT
      context.textBaseline = 'middle'
      context.textAlign = 'right'
      for (let hour = 0; hour <= 24; hour += AXIS_LABEL_STEP_HOURS) {
        const y = (hour / 24) * height
        context.fillStyle = gridColor
        context.fillRect(plotLeft, y, plotWidth, 1)
        context.fillStyle = labelColor
        // Clamped inwards: a label half outside the canvas at 00 and 24 is a clipped number.
        context.fillText(String(hour).padStart(2, '0'), plotLeft - 4, Math.min(Math.max(y, 5), height - 5))
      }

      if (!selectedDate) return
      const selectedIndex = profile.days.findIndex((day) => day.date === selectedDate)
      if (selectedIndex < 0) return

      const x = plotLeft + (selectedIndex + 0.5) * columnWidth
      context.strokeStyle = theme.palette.text.primary
      context.lineWidth = 1
      context.setLineDash([3, 3])
      context.beginPath()
      context.moveTo(x, 0)
      context.lineTo(x, height)
      context.stroke()
      context.setLineDash([])

      // The selected day's own crossings, so the marked day can be read rather than only located.
      const selected = profile.days[selectedIndex]
      const daylight = selected.segments.find((segment) => segment.phase === 'day')
      if (!daylight) return
      const spanMinutes = selected.segments[selected.segments.length - 1]?.endMinute || 1
      const alignRight = x > width - 40
      context.textAlign = alignRight ? 'right' : 'left'
      context.fillStyle = theme.palette.text.primary
      for (const [minute, offset] of [
        [daylight.startMinute, -8],
        [daylight.endMinute, 8],
      ] as const) {
        const y = Math.min(Math.max((minute / spanMinutes) * height + offset, 6), height - 6)
        context.fillText(formatLocalMinute(minute), x + (alignRight ? -4 : 4), y)
      }
    }

    draw()

    if (typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver(draw)
    observer.observe(canvas)
    return () => observer.disconnect()
  }, [profile, selectedDate, colors, theme])

  if (!site || !profile || !moment) return null

  const description = copy.dayLengthChartDescription(profile.year, profile.hemisphere)

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.75 }}>
      <Typography sx={compactLabelSx}>{copy.dayLengthChartCaption(site.latitude, profile.hemisphere)}</Typography>

      {/* Not colour alone: the bands are named here, and described for assistive technology. */}
      <Box
        component="canvas"
        ref={canvasRef}
        role="img"
        aria-label={description}
        data-day-count={profile.days.length}
        data-selected-date={selectedDate ?? undefined}
        sx={{ display: 'block', width: '100%', height: `${CHART_HEIGHT_PX}px`, borderRadius: 0.5 }}
      />

      <Stack direction="row" spacing={1} sx={{ flexWrap: 'wrap', rowGap: 0.25 }}>
        {PHASES.map(({ phase, label }) => (
          <Stack key={phase} direction="row" spacing={0.5} sx={{ alignItems: 'center' }}>
            <Box sx={{ width: 8, height: 8, borderRadius: 0.25, bgcolor: colors[phase] }} />
            <Typography sx={compactLabelSx}>{label}</Typography>
          </Stack>
        ))}
      </Stack>

      <Typography sx={compactLabelSx}>
        {copy.dayLengthChartSelectedDateLabel}: {formatShortDate(selectedDate!)}
      </Typography>
      <Typography sx={compactLabelSx}>
        {copy.longestDayLabel}: {formatShortDate(profile.longestDay.date)} ·{' '}
        {Math.floor(profile.longestDay.minutes / 60)} h {profile.longestDay.minutes % 60} min
      </Typography>
      <Typography sx={compactLabelSx}>
        {copy.shortestDayLabel}: {formatShortDate(profile.shortestDay.date)} ·{' '}
        {Math.floor(profile.shortestDay.minutes / 60)} h {profile.shortestDay.minutes % 60} min
      </Typography>
    </Box>
  )
}
