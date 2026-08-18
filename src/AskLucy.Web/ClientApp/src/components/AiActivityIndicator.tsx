import { Box, keyframes, Stack, Typography } from '@mui/material'
import { usePrefersReducedMotion } from '../hooks/usePrefersReducedMotion'

export type AiActivityState = 'thinking' | 'streaming' | 'tool-executing'

interface AiActivityIndicatorProps {
  state: AiActivityState
  label?: string
}

const bounce = keyframes`
  0%, 80%, 100% { transform: scale(0.6); opacity: 0.4; }
  40% { transform: scale(1); opacity: 1; }
`

const pulse = keyframes`
  0%, 100% { opacity: 0.35; }
  50% { opacity: 1; }
`

const DOT_ANIMATION_DELAYS = ['0s', '0.15s', '0.3s']

const DEFAULT_LABEL: Record<AiActivityState, string> = {
  thinking: 'Ask Lucy is thinking',
  streaming: 'Ask Lucy is responding',
  'tool-executing': 'Running a tool',
}

/** Shared "the AI is doing something" presentation (FR-007), generalized from chat's
 * `ThinkingIndicator` — the `thinking`/`streaming` states render the same three-dot motif;
 * `tool-executing` renders a single pulsing dot beside its label, since tool/document
 * processing status already carries its own detailed progress UI elsewhere and only needs
 * a lightweight "in progress" marker here. Respects `prefers-reduced-motion` (FR-010) by
 * dropping to a static, non-animated presentation. */
export function AiActivityIndicator({ state, label }: AiActivityIndicatorProps) {
  const prefersReducedMotion = usePrefersReducedMotion()
  const accessibleLabel = label ?? DEFAULT_LABEL[state]

  return (
    <Stack
      direction="row"
      role="status"
      aria-live="polite"
      aria-label={accessibleLabel}
      spacing={0.75}
      sx={{ display: 'inline-flex', alignItems: 'center', px: 2, py: 1.5 }}
    >
      {state === 'tool-executing' ? (
        <>
          <Box
            sx={{
              width: 8,
              height: 8,
              borderRadius: '50%',
              bgcolor: 'secondary.main',
              animation: prefersReducedMotion ? 'none' : `${pulse} 1.4s ease-in-out infinite`,
              opacity: prefersReducedMotion ? 1 : undefined,
            }}
          />
          <Typography variant="body2" color="text.secondary">
            {accessibleLabel}
          </Typography>
        </>
      ) : (
        DOT_ANIMATION_DELAYS.map((delay) => (
          <Box
            key={delay}
            sx={{
              width: 8,
              height: 8,
              borderRadius: '50%',
              bgcolor: 'text.secondary',
              animation: prefersReducedMotion ? 'none' : `${bounce} 1.2s ease-in-out infinite`,
              animationDelay: delay,
              opacity: prefersReducedMotion ? 0.8 : undefined,
            }}
          />
        ))
      )}
    </Stack>
  )
}
