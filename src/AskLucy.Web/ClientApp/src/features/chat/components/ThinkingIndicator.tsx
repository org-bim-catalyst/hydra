import { Box, keyframes } from '@mui/material'

const bounce = keyframes`
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
`

const DOT_ANIMATION_DELAYS = ['0s', '0.15s', '0.3s']

/**
 * Animated three-dot "thinking" indicator shown in place of the assistant's reply bubble
 * while a send is in flight and no content has streamed in yet (FR-006/FR-007). Always
 * animates — no reduced-motion fallback variant is provided (FR-011, spec clarification).
 */
export function ThinkingIndicator() {
  return (
    <Box
      role="status"
      aria-live="polite"
      aria-label="Ask Lucy is thinking"
      sx={{ display: 'inline-flex', alignItems: 'center', gap: 0.75, px: 2, py: 1.5 }}
    >
      {DOT_ANIMATION_DELAYS.map((delay) => (
        <Box
          key={delay}
          sx={{
            width: 8,
            height: 8,
            borderRadius: '50%',
            bgcolor: 'text.secondary',
            animation: `${bounce} 1.2s ease-in-out infinite`,
            animationDelay: delay,
          }}
        />
      ))}
    </Box>
  )
}
