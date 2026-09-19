import { Box, IconButton, InputBase, Slider, Typography } from '@mui/material'
import { RiPauseFill, RiPlayFill } from '@remixicon/react'
import { useId } from 'react'
import {
  COMPACT_ACCENT,
  COMPACT_MONO_FONT,
  compactInputSx,
  compactLabelSx,
} from '../../../viewer/panels/chrome/compactStyles'
import { copy } from '../copy'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

const ROW_LABEL_WIDTH = 34

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
 *
 * Laid out as the reference page's two compact rows — date, then play/slider/time — with small
 * labels and a monospace amber time readout.
 */
export function SolarTimeControlPanel(): React.JSX.Element | null {
  const dateInputId = useId()
  const moment = useSolarAnalysisStore((s) => s.moment)
  const setLocalDate = useSolarAnalysisStore((s) => s.setLocalDate)
  const setLocalMinuteOfDay = useSolarAnalysisStore((s) => s.setLocalMinuteOfDay)
  const setPlaying = useSolarAnalysisStore((s) => s.setPlaying)

  if (!moment) return null

  const localTimeLabel = formatLocalTime(moment.localMinuteOfDay)

  return (
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <Box component="label" htmlFor={dateInputId} sx={{ ...compactLabelSx, width: ROW_LABEL_WIDTH, flexShrink: 0 }}>
          {copy.dateLabel}
        </Box>
        <InputBase
          id={dateInputId}
          type="date"
          value={moment.localDate}
          onChange={(e) => setLocalDate(e.target.value)}
          sx={compactInputSx}
        />
      </Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 1.5 }}>
        <IconButton
          aria-label={moment.isPlaying ? copy.stopLabel : copy.playLabel}
          onClick={() => setPlaying(!moment.isPlaying)}
          size="small"
          sx={{ width: 28, height: 28, flexShrink: 0, border: '1px solid', borderColor: 'divider', borderRadius: '5px' }}
        >
          {moment.isPlaying ? <RiPauseFill size={14} /> : <RiPlayFill size={14} />}
        </IconButton>
        <Slider
          size="small"
          aria-label={copy.timeLabel}
          aria-valuetext={localTimeLabel}
          value={moment.localMinuteOfDay}
          min={0}
          max={1439}
          step={1}
          onChange={(_, value) => setLocalMinuteOfDay(value as number)}
          sx={{ flex: 1, color: COMPACT_ACCENT }}
        />
        <Typography
          component="span"
          sx={{ fontFamily: COMPACT_MONO_FONT, fontSize: 12.5, color: COMPACT_ACCENT, minWidth: 44, textAlign: 'right', flexShrink: 0 }}
        >
          {localTimeLabel}
        </Typography>
      </Box>
    </Box>
  )
}
