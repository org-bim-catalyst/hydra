import { RiCloseLine, RiVolumeMuteLine, RiVolumeUpLine } from '@remixicon/react'
import { Box, IconButton, Stack, Tooltip, Typography } from '@mui/material'
import { lazy, Suspense } from 'react'
import { LucyPortrait } from '../branding/LucyPortrait'

const SceneBackground = lazy(() =>
  import('../scene/SceneBackground').then((m) => ({ default: m.SceneBackground })),
)

export interface ContinuousVoiceViewProps {
  /** Drives the reactive presence visualization — the same `getReactiveIntensity` shape
   * `AiPresenceCard` already consumes, sourced here from this view's own
   * `useConversationAudio` instance rather than the small persistent card's. */
  getReactiveIntensity: () => number
  /** A short, user-facing status line (e.g. "Listening…", "Thinking…", "Speaking…") derived
   * from `useConversationAudio`'s `voiceState` by the caller — kept as a plain string here so
   * this component stays a simple presentational shell, not another place that interprets
   * `VoiceStateName`. */
  statusLabel: string
  errorMessage: string | null
  isMuted: boolean
  onToggleMute: () => void
  onExit: () => void
}

/**
 * specs/034-transcription-crash-gesture-and-continuous-view FR-008/FR-009/FR-010 — the
 * dedicated, focused view Continuous mode opens into: Lucy's reactive presence (reusing
 * `AiPresenceCard`'s existing `SceneBackground` lazy-import pattern, at full scale here rather
 * than the small persistent card's) plus exactly two controls, Exit and Mute. No text composer,
 * attach, insert-prompt, send, or mode-switch control is reachable from this view — activating
 * it is a full takeover of the chat panel area (spec.md Assumptions), not an overlay alongside
 * the normal composer.
 */
export function ContinuousVoiceView({
  getReactiveIntensity,
  statusLabel,
  errorMessage,
  isMuted,
  onToggleMute,
  onExit,
}: ContinuousVoiceViewProps) {
  return (
    <Box
      sx={{
        position: 'relative',
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        bgcolor: 'background.default',
      }}
    >
      <Tooltip title="Exit voice conversation">
        <IconButton
          onClick={onExit}
          aria-label="Exit voice conversation"
          sx={{ position: 'absolute', top: 16, right: 16 }}
        >
          <RiCloseLine />
        </IconButton>
      </Tooltip>

      <Box
        sx={{
          width: 'min(60vh, 420px)',
          height: 'min(60vh, 420px)',
          minWidth: 220,
          minHeight: 220,
          borderRadius: '50%',
          overflow: 'hidden',
        }}
      >
        <Suspense
          fallback={
            <Box
              sx={{
                position: 'absolute',
                inset: 0,
                display: 'flex',
                alignItems: 'center',
                justifyContent: 'center',
              }}
            >
              <LucyPortrait variant="auth" alt="Lucy" />
            </Box>
          }
        >
          <SceneBackground getReactiveIntensity={getReactiveIntensity} />
        </Suspense>
      </Box>

      <Stack spacing={1} sx={{ alignItems: 'center', mt: 3 }}>
        <Typography variant="body2" color="text.secondary" role="status">
          {statusLabel}
        </Typography>
        {errorMessage && (
          <Typography variant="body2" color="error" role="alert">
            {errorMessage}
          </Typography>
        )}
      </Stack>

      <Tooltip title={isMuted ? 'Unmute Lucy' : 'Mute Lucy'}>
        <IconButton
          onClick={onToggleMute}
          aria-label={isMuted ? 'Unmute Lucy' : 'Mute Lucy'}
          color={isMuted ? 'error' : 'default'}
          sx={{ mt: 4 }}
        >
          {isMuted ? <RiVolumeMuteLine /> : <RiVolumeUpLine />}
        </IconButton>
      </Tooltip>
    </Box>
  )
}
