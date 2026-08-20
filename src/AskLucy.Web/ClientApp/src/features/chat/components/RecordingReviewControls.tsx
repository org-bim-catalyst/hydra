import { RiCheckLine, RiCloseLine, RiSendPlane2Fill } from '@remixicon/react'
import { IconButton, Tooltip } from '@mui/material'
import type { RecordingPhase } from '../voice/useVoiceRecorder'

export interface RecordingReviewControlsProps {
  phase: RecordingPhase
  onFinish: () => void
  onCancelRecording: () => void
  onAccept: () => void
  /** `'row'` for the Expanded panel's `ChatComposer` (specs/029-fix-chat-widget-bugs
   * research.md Decision 5 — previously `VoiceControlBar`'s horizontal layout, now
   * retired); the Collapsed vertical stack supplies its own `Stack` wrapper and doesn't
   * need this component to add spacing. */
  placement?: 'left' | 'right'
}

/**
 * FR-020–FR-023: the finish/cancel/send controls shown while a Push-to-Talk recording is
 * in progress or awaiting review — identical markup and semantics regardless of which
 * layout (`CollapsedVoiceControls` or `VoiceControlBar`) renders it, per research.md #10.
 */
export function RecordingReviewControls({
  phase,
  onFinish,
  onCancelRecording,
  onAccept,
  placement = 'right',
}: RecordingReviewControlsProps) {
  if (phase === 'idle') return null

  const finish = phase === 'recording' && (
    <Tooltip title="Finished speaking" placement={placement}>
      <IconButton onClick={onFinish} aria-label="Finished speaking" size="small">
        <RiCheckLine fontSize="small" />
      </IconButton>
    </Tooltip>
  )

  const accept = phase === 'reviewing' && (
    <Tooltip title="Send for transcription" placement={placement}>
      <IconButton
        onClick={onAccept}
        aria-label="Send recording for transcription"
        size="small"
        color="primary"
      >
        <RiSendPlane2Fill fontSize="small" />
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
      {finish}
      {accept}
      {cancel}
    </>
  )
}
