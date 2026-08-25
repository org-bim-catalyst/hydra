import { RiCheckLine, RiCloseLine } from '@remixicon/react'
import { IconButton, Tooltip } from '@mui/material'
import type { RecordingPhase } from '../voice/useVoiceRecorder'

export interface RecordingReviewControlsProps {
  phase: RecordingPhase
  onFinish: () => void
  onCancelRecording: () => void
  /** Optional content to render between cancel and finish (e.g. the live waveform in the
   * `ChatComposer` tap-review row — specs/040 US3). `CollapsedVoiceControls` omits this. */
  middle?: React.ReactNode
  /** Tooltip placement — defaults to `'bottom'` (specs/040 US7 normalises all composer/voice-control tooltips). */
  placement?: 'left' | 'right' | 'bottom'
}

/**
 * specs/026-floating-chat-assistant FR-020/FR-021, specs/031-voice-controls-redesign
 * research.md Decision 1 — the finish/cancel controls shown while a Push-to-Talk
 * recording is actively in progress, identical markup and semantics regardless of which
 * layout (`CollapsedVoiceControls` or the Expanded panel's `ChatComposer`) renders it, per
 * research.md #10 (specs/026). Finish now stops and transcribes in one step — there is no
 * longer a separate manual "send for transcription" control between recording and the
 * transcript landing in the message field.
 *
 * specs/040 US3: render order is cancel → middle → finish (Figure 3), so the waveform
 * straddles the two controls rather than both buttons trailing the waveform.
 */
export function RecordingReviewControls({
  phase,
  onFinish,
  onCancelRecording,
  middle,
  placement = 'bottom',
}: RecordingReviewControlsProps) {
  if (phase === 'idle') return null

  const finish = phase === 'recording' && (
    <Tooltip title="Finished speaking" placement={placement}>
      <IconButton onClick={onFinish} aria-label="Finished speaking" size="small">
        <RiCheckLine fontSize="small" />
      </IconButton>
    </Tooltip>
  )

  const cancel = (
    <Tooltip title="Cancel recording" placement={placement}>
      <IconButton onClick={onCancelRecording} aria-label="Cancel recording" size="small">
        <RiCloseLine fontSize="small" />
      </IconButton>
    </Tooltip>
  )

  return (
    <>
      {cancel}
      {middle}
      {finish}
    </>
  )
}
