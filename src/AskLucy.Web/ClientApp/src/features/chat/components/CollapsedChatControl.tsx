import { RiDraggable } from '@remixicon/react'
import { Box, IconButton, Stack, Tooltip, Typography } from '@mui/material'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'
import { radius } from '../../../theme'
import { VoiceAnalyzer, type VoiceAnalyzerState } from './VoiceAnalyzer'
import { CollapsedVoiceControls, type VoiceControlsProps } from './CollapsedVoiceControls'

export interface CollapsedChatControlProps {
  onExpand: () => void
  analyzerState: VoiceAnalyzerState
  getIntensity: () => number
  voiceControls: VoiceControlsProps
  triggerRef?: React.Ref<HTMLButtonElement>
  contentId: string
}

const STATUS_LABEL: Record<VoiceAnalyzerState, string> = {
  idle: 'Idle',
  processing: 'Processing',
  speaking: 'Speaking',
  listening: 'Listening',
}

/** FR-002/FR-003/FR-005: the default, narrow, always-visible collapsed presentation of
 * the chat widget — handle, voice analyzer, voice controls, status label, and nothing
 * else (contracts/chat-widget-components.md's `CollapsedChatControlProps`). Sized via
 * MUI breakpoints so it never overlaps the workspace's other floating controls at any
 * viewport width (research.md #11). */
export function CollapsedChatControl({
  onExpand,
  analyzerState,
  getIntensity,
  voiceControls,
  triggerRef,
  contentId,
}: CollapsedChatControlProps) {
  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 1,
        width: { xs: 56, sm: 64 },
        py: 1.5,
        px: 1,
        borderRadius: `${radius.lg}px`,
        bgcolor: CIRCULAR_ACTION_CHROME.collapsedBg,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
        color: CIRCULAR_ACTION_CHROME.icon,
      }}
    >
      <Tooltip title="Expand Ask Lucy" placement="left">
        <IconButton
          ref={triggerRef}
          onClick={onExpand}
          aria-label="Expand Ask Lucy assistant"
          aria-expanded={false}
          aria-controls={contentId}
          size="small"
          sx={{ color: CIRCULAR_ACTION_CHROME.icon }}
        >
          <RiDraggable fontSize="small" />
        </IconButton>
      </Tooltip>

      <VoiceAnalyzer state={analyzerState} getIntensity={getIntensity} />

      <CollapsedVoiceControls {...voiceControls} />

      <Stack spacing={0.25} sx={{ alignItems: 'center', mt: 0.5 }}>
        <Box
          sx={{
            width: 6,
            height: 6,
            borderRadius: '50%',
            bgcolor:
              analyzerState === 'idle'
                ? 'text.disabled'
                : analyzerState === 'processing'
                  ? 'warning.main'
                  : 'success.main',
          }}
        />
        <Typography
          variant="caption"
          sx={{ fontSize: '0.6rem', lineHeight: 1, letterSpacing: 0.4, opacity: 0.8 }}
        >
          {STATUS_LABEL[analyzerState].toUpperCase()}
        </Typography>
      </Stack>
    </Box>
  )
}
