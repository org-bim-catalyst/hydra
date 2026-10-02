import { Box, LinearProgress, Stack, Typography } from '@mui/material'

export interface AudioLevelMeterProps {
  /** e.g. "Input: Realtek Microphone" or "Output: System default". */
  label: string
  /** 0–1 RMS level; ignored (shown dim) while `active` is false. */
  level?: number
  /** Whether the device is actually recording/playing right now. */
  active: boolean
}

/**
 * A thin live level bar next to a device label (specs/078 follow-up) — lets an administrator see
 * at a glance whether the connected mic/speaker is actually carrying signal (bar moves, green)
 * versus silently failing (active but flat, amber) before they wait on a transcript or a reply.
 */
export function AudioLevelMeter({ label, level = 0, active }: AudioLevelMeterProps) {
  const clamped = Math.min(100, Math.max(0, Math.round(level * 100)))
  const hasSignal = clamped > 2

  return (
    <Stack direction="row" spacing={1.5} sx={{ alignItems: 'center' }}>
      <Typography variant="caption" color="text.secondary" sx={{ minWidth: 220 }}>
        {label}
      </Typography>
      <Box sx={{ flexGrow: 1, maxWidth: 160 }}>
        <LinearProgress
          variant="determinate"
          value={active ? clamped : 0}
          color={active ? (hasSignal ? 'success' : 'warning') : 'inherit'}
          aria-label={`${label} level`}
          sx={{ height: 8, borderRadius: 4, opacity: active ? 1 : 0.35 }}
        />
      </Box>
    </Stack>
  )
}
