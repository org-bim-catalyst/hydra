import { Box, IconButton, Slider, TextField, Typography } from '@mui/material'
import { RiPauseFill, RiPlayFill } from '@remixicon/react'
import { z } from 'zod'
import { copy } from '../copy'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

export const SOLAR_TIME_CONTROL_TYPE_KEY = 'solar.time-control'

/** contracts/solar-panels.md — this live panel is driven entirely by `solarAnalysisStore`
 * (research D12: "interactive code with their own state", which is exactly why it is a live
 * panel and not content). `data` carries nothing the store doesn't already own; the schema exists
 * only to satisfy `PanelTypeDefinition`'s contract. */
export const solarTimeControlDataSchema = z.object({})
export type SolarTimeControlData = z.infer<typeof solarTimeControlDataSchema>

function formatLocalTime(localMinuteOfDay: number): string {
  const hour = Math.floor(localMinuteOfDay / 60)
  const minute = localMinuteOfDay % 60
  return `${String(hour).padStart(2, '0')}:${String(minute).padStart(2, '0')}`
}

/**
 * contracts/solar-panels.md "Time Control" — date picker, 0…1439 local-minute slider, play/stop,
 * speed (FR-020, FR-021). Every control writes only `instantUtc` (via the store's setters), so sun
 * position, shadows and figures can never disagree — there is one value, not three (FR-022).
 * The slider is a native `range` input with `aria-valuetext` carrying the local time, so assistive
 * technology reads "14:00", not "840" (FR-032).
 */
export function SolarTimeControlPanel(): React.JSX.Element | null {
  const moment = useSolarAnalysisStore((s) => s.moment)
  const setLocalDate = useSolarAnalysisStore((s) => s.setLocalDate)
  const setLocalMinuteOfDay = useSolarAnalysisStore((s) => s.setLocalMinuteOfDay)
  const setPlaying = useSolarAnalysisStore((s) => s.setPlaying)

  if (!moment) return null

  const localTimeLabel = formatLocalTime(moment.localMinuteOfDay)

  return (
    <Box sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <TextField
        type="date"
        label={copy.dateLabel}
        value={moment.localDate}
        onChange={(e) => setLocalDate(e.target.value)}
        size="small"
        fullWidth
      />
      <Box sx={{ display: 'flex', flexDirection: 'row', gap: 1, alignItems: 'center' }}>
        <IconButton
          aria-label={moment.isPlaying ? copy.stopLabel : copy.playLabel}
          onClick={() => setPlaying(!moment.isPlaying)}
          size="small"
        >
          {moment.isPlaying ? <RiPauseFill /> : <RiPlayFill />}
        </IconButton>
        <Box sx={{ flex: 1 }}>
          <Slider
            aria-label={copy.timeLabel}
            aria-valuetext={localTimeLabel}
            value={moment.localMinuteOfDay}
            min={0}
            max={1439}
            step={1}
            onChange={(_, value) => setLocalMinuteOfDay(value as number)}
          />
        </Box>
        <Typography variant="body2" sx={{ fontFamily: 'monospace', minWidth: 48, textAlign: 'right' }}>
          {localTimeLabel}
        </Typography>
      </Box>
    </Box>
  )
}
