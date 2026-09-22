import { alpha, Alert, Box, IconButton, InputBase, Slider, type SliderProps } from '@mui/material'
import { RiArrowDownSFill, RiArrowUpSFill, RiPauseFill, RiPlayFill } from '@remixicon/react'
import { useEffect, useId, useMemo, useRef, useState } from 'react'
import {
  compactAlertSx,
  COMPACT_ACCENT,
  COMPACT_MONO_FONT,
  compactInputSx,
  compactLabelSx,
} from '../../../viewer/panels/chrome/compactStyles'
import { copy } from '../copy'
import { FALLBACK_TIME_ZONE, fromLocalParts, toLocalParts } from '../solar/timeZone'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'
import { buildTimeSliderMarks, formatLocalTime, formatTimeDraftInput, MINUTES_PER_DAY, parseLocalTimeEntry, snapToQuarterHour } from './timeEntry'

/** Row 2's fixed content height (matches the play/stop button) — the slider is vertically centered
 * within it so tick labels rendered beneath the rail never shift where the button and readout sit,
 * and never overhang past the button on the left (item 2/4 of the 2026-09 screenshot review). */
const TIME_ROW_HEIGHT = 28
const TIME_ROW_SLIDER_INSET = 10
const ROW_LABEL_WIDTH = 34

/** The keys MUI Slider treats as keyboard stepping — used to tell a keyboard interaction from a
 * pointer one at `keydown` (research D3). */
const SLIDER_STEP_KEYS = new Set(['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown', 'Home', 'End', 'PageUp', 'PageDown'])

/**
 * contracts/solar-panels.md "Time Control" — date picker, 0…1439 local-minute slider, play/stop,
 * speed (FR-020, FR-021). Every control writes only `instantUtc` (via the store's setters), so sun
 * position, shadows and figures can never disagree — there is one value, not three (FR-022).
 * The slider is a native `range` input with `aria-valuetext` carrying the local time, so assistive
 * technology reads "14:00", not "840" (FR-032).
 *
 * specs/064-precise-time-control adds three things without adding a second time value (FR-020 of
 * that spec): a typed entry beside the slider (contracts/time-entry.md), 15-minute tick marks
 * (research D4), and drag-snapping paired with unrestricted 1-minute keyboard stepping (research
 * D3). All the decision logic those three add lives in the pure `./timeEntry` module; this
 * component only wires it to the store and the DOM.
 *
 * Laid out as the reference page's two compact rows — date, then play/slider/time — with small
 * labels and a monospace amber time readout.
 */
export function SolarTimeControlPanel(): React.JSX.Element | null {
  const dateInputId = useId()
  const site = useSolarAnalysisStore((s) => s.site)
  const moment = useSolarAnalysisStore((s) => s.moment)
  const setLocalDate = useSolarAnalysisStore((s) => s.setLocalDate)
  const setLocalMinuteOfDay = useSolarAnalysisStore((s) => s.setLocalMinuteOfDay)
  const setPlaying = useSolarAnalysisStore((s) => s.setPlaying)

  // data-model.md "The draft entry" — text typed but not yet committed. `null` means Idle: the
  // field shows the current time, following the store on every route by which it can change
  // (slider, keyboard, playback, or a previous commit) — the property T012/T018 exist to pin.
  const [draft, setDraft] = useState<string | null>(null)
  const [entryError, setEntryError] = useState<string | null>(null)
  const previousLocalDateRef = useRef<string | undefined>(moment?.localDate)

  // research D3 — which kind of interaction is in progress, so `onChangeCommitted` can snap a
  // drag release but leave a keyboard step alone. A ref, not state: it must not cause a render.
  const dragSourceRef = useRef<'pointer' | 'keyboard' | null>(null)

  const sliderTrackRef = useRef<HTMLDivElement | null>(null)
  const [trackWidthPx, setTrackWidthPx] = useState<number | null>(null)

  useEffect(() => {
    const node = sliderTrackRef.current
    // research D5 — no ResizeObserver (or nothing to observe yet) leaves trackWidthPx at null,
    // which buildTimeSliderMarks treats as the Full tier, not the empty one.
    if (!node || typeof ResizeObserver === 'undefined') return
    const observer = new ResizeObserver((entries) => {
      const width = entries[0]?.contentRect.width
      if (width !== undefined) setTrackWidthPx(width)
    })
    observer.observe(node)
    return () => observer.disconnect()
  }, [])

  // data-model.md "The rule that keeps it honest" #3 / FR-010 — a stale draft must not be applied
  // to a date the user did not type it for. Must run before the early return below, since hooks
  // cannot be conditional.
  useEffect(() => {
    if (moment && moment.localDate !== previousLocalDateRef.current) {
      setDraft(null)
      setEntryError(null)
    }
    previousLocalDateRef.current = moment?.localDate
  }, [moment])

  const sliderMarks = useMemo(() => buildTimeSliderMarks(trackWidthPx), [trackWidthPx])
  const hourMarkSelector = useMemo(
    () =>
      sliderMarks
        .map((mark, index) => (mark.isHour ? `& .MuiSlider-mark[data-index="${index}"]` : null))
        .filter((selector): selector is string => selector !== null)
        .join(', '),
    [sliderMarks],
  )

  if (!moment) return null

  const localTimeLabel = formatLocalTime(moment.localMinuteOfDay)
  const timeZoneId = site?.timeZoneId ?? FALLBACK_TIME_ZONE

  // research D2 — round-trip the candidate through the site's own conversion functions rather than
  // writing new DST logic: if the instant it resolves to projects back to a *different* local
  // minute, the candidate falls in a spring-forward gap and does not exist on this date. Shared by
  // the typed-entry commit and the spinner/arrow-key step below — one rule, not two.
  function commitMinute(minuteOfDay: number): boolean {
    const candidateInstant = fromLocalParts(moment!.localDate, minuteOfDay, timeZoneId)
    const roundTrip = toLocalParts(candidateInstant, timeZoneId)
    if (roundTrip.localMinuteOfDay !== minuteOfDay) {
      setEntryError(copy.timeEntryNonexistent)
      return false
    }

    setEntryError(null)
    setDraft(null)
    setPlaying(false) // FR-009 — a named instant should not be swept away by playback within a frame.
    setLocalMinuteOfDay(minuteOfDay) // research D1 — the store's existing setter; no second value.
    return true
  }

  function commitDraft() {
    if (draft === null) return // Idle already — nothing typed since the last commit or sync.

    const parsed = parseLocalTimeEntry(draft)
    if (!parsed.ok) {
      setEntryError(parsed.reason === 'malformed' ? copy.timeEntryMalformed : copy.timeEntryOutOfRange)
      return // FR-003/FR-005 — the typed text and the previous time both stay exactly as they were.
    }

    commitMinute(parsed.minuteOfDay)
  }

  // Spinner buttons + up/down arrow keys (the "up/down arrows to increase/decrease without
  // damaging the clock format" alternative from the 2026-09 screenshot review) — steps from
  // whatever is currently typed if it already parses, else from the last committed time, so a
  // half-typed valid entry can be nudged instead of discarded.
  function stepMinute(deltaMinutes: number) {
    const draftParsed = draft !== null ? parseLocalTimeEntry(draft) : null
    const base = draftParsed?.ok ? draftParsed.minuteOfDay : moment!.localMinuteOfDay
    const next = ((base + deltaMinutes) % MINUTES_PER_DAY + MINUTES_PER_DAY) % MINUTES_PER_DAY
    commitMinute(next)
  }

  const handleSliderChangeCommitted: NonNullable<SliderProps['onChangeCommitted']> = (_, value) => {
    const source = dragSourceRef.current
    dragSourceRef.current = null
    if (source === 'pointer') {
      setLocalMinuteOfDay(snapToQuarterHour(value as number))
    }
    // A keyboard-driven commit needs no further action: onChange already applied the unsnapped,
    // one-minute-stepped value (FR-017).
  }

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
        <Box
          ref={sliderTrackRef}
          onPointerDownCapture={() => {
            dragSourceRef.current = 'pointer'
          }}
          onKeyDownCapture={(e) => {
            if (SLIDER_STEP_KEYS.has(e.key)) dragSourceRef.current = 'keyboard'
          }}
          sx={{
            flex: 1,
            display: 'flex',
            alignItems: 'center',
            height: TIME_ROW_HEIGHT,
            px: `${TIME_ROW_SLIDER_INSET}px`,
          }}
        >
          <Slider
            size="small"
            aria-label={copy.timeLabel}
            aria-valuetext={localTimeLabel}
            value={moment.localMinuteOfDay}
            min={0}
            max={1439}
            step={1}
            marks={sliderMarks}
            onChange={(_, value) => setLocalMinuteOfDay(value as number)}
            onChangeCommitted={handleSliderChangeCommitted}
            sx={{
              flex: 1,
              color: COMPACT_ACCENT,
              // MUI's `marked` variant (Slider.js) adds a 20px marginBottom whenever `marks` is
              // non-empty, to reserve room for markLabel text below the rail. That shifted the rail
              // upward within our fixed-height row (2026-09 screenshot review, item 2/4) — the row
              // already allows the labels to overflow its fixed height instead, so the reservation
              // is cancelled here rather than compensated for with an offset elsewhere.
              marginBottom: 0,
              '& .MuiSlider-mark': {
                width: 2,
                height: 4,
                borderRadius: 0,
                bgcolor: (theme) => alpha(theme.palette.text.primary, 0.25),
              },
              '& .MuiSlider-markLabel': {
                fontSize: 9,
                fontFamily: COMPACT_MONO_FONT,
                color: 'text.secondary',
              },
              ...(hourMarkSelector && {
                [hourMarkSelector]: {
                  width: 2,
                  height: 8,
                  bgcolor: (theme) => alpha(theme.palette.text.primary, 0.6),
                },
              }),
            }}
          />
        </Box>
        <Box sx={{ display: 'flex', alignItems: 'center', flexShrink: 0 }}>
          <InputBase
            value={draft ?? localTimeLabel}
            onChange={(e) => {
              // FR: auto-inserts the ":" as digits accumulate (H, HH, HH:M, HH:MM) so typing never
              // requires the separator itself, without changing what commitDraft validates.
              setDraft(formatTimeDraftInput(e.target.value))
              setEntryError(null) // data-model.md "Rejected -> user edits again -> Editing"
            }}
            onFocus={(e) => e.target.select()} // typing immediately replaces the shown time, no manual clear first
            onBlur={commitDraft}
            onKeyDown={(e) => {
              if (e.key === 'Enter') commitDraft()
              else if (e.key === 'ArrowUp') {
                e.preventDefault()
                stepMinute(1)
              } else if (e.key === 'ArrowDown') {
                e.preventDefault()
                stepMinute(-1)
              }
            }}
            disabled={moment.isPlaying} // item 5 of the 2026-09 screenshot review — no typing while playing
            slotProps={{
              input: {
                'aria-label': copy.timeLabel,
                'aria-invalid': entryError !== null,
                inputMode: 'numeric',
                maxLength: 5,
              },
            }}
            sx={{
              ...compactInputSx,
              fontFamily: COMPACT_MONO_FONT,
              fontSize: 12.5,
              color: COMPACT_ACCENT,
              width: 60,
              height: TIME_ROW_HEIGHT,
              flexShrink: 0,
              textAlign: 'right',
              boxSizing: 'border-box',
              borderTopRightRadius: 0,
              borderBottomRightRadius: 0,
              borderRight: 'none',
              '& .MuiInputBase-input': { textAlign: 'right', px: 1, py: 0 },
            }}
          />
          <Box
            sx={{
              display: 'flex',
              flexDirection: 'column',
              height: TIME_ROW_HEIGHT,
              boxSizing: 'border-box',
              border: '1px solid',
              borderColor: 'divider',
              borderTopRightRadius: '5px',
              borderBottomRightRadius: '5px',
            }}
          >
            <IconButton
              aria-label={copy.timeIncreaseLabel}
              onClick={() => stepMinute(1)}
              disabled={moment.isPlaying}
              size="small"
              sx={{ width: 16, height: TIME_ROW_HEIGHT / 2, borderRadius: 0 }}
            >
              <RiArrowUpSFill size={10} />
            </IconButton>
            <IconButton
              aria-label={copy.timeDecreaseLabel}
              onClick={() => stepMinute(-1)}
              disabled={moment.isPlaying}
              size="small"
              sx={{ width: 16, height: TIME_ROW_HEIGHT / 2, borderRadius: 0 }}
            >
              <RiArrowDownSFill size={10} />
            </IconButton>
          </Box>
        </Box>
      </Box>
      {entryError && (
        <Alert severity="error" sx={compactAlertSx}>
          {entryError}
        </Alert>
      )}
    </Box>
  )
}
