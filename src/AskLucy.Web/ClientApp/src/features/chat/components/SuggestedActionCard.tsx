import { useId, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Chip,
  FormControlLabel,
  Paper,
  Radio,
  RadioGroup,
  Stack,
  Typography,
} from '@mui/material'
import type { SuggestedAction } from '../api/aiApi'
import { radius } from '../../../theme'

export interface SuggestedActionCardProps {
  question: string
  actions: SuggestedAction[]
  /** false → inert history rendering (an earlier offer, or the feature disabled) — plain text, no radios, no submit, not focusable. */
  isLive: boolean
  /** Rejected promises must be handled by the caller (`ChatPage.tsx`); this card never fires and forgets — it hands off and lets `error`/`isSubmitting` report back. */
  onSelect: (action: SuggestedAction) => Promise<void>
  isSubmitting: boolean
  /** Rendered as an inline Alert inside the card. */
  error: string | null
}

/**
 * specs/045-conversational-agent-runtime US2/US3 (T075), contracts/suggested-actions-api.md §3 —
 * the option list closing a turn, modelled on Claude Code's `AskUserQuestion` card (the UX
 * reference the original request named).
 *
 * <p>No free-text row: the composer is live throughout (FR-031), so typing instead of selecting
 * is always available and needs no row of its own here.</p>
 */
export function SuggestedActionCard({ question, actions, isLive, onSelect, isSubmitting, error }: SuggestedActionCardProps) {
  const [selectedIndex, setSelectedIndex] = useState<number | null>(null)
  const questionId = useId()

  if (!isLive) {
    // FR-032/history — an earlier or disabled-feature offer renders as plain text: the question
    // and the option labels as a bulleted list, never as interactive rows.
    return (
      <Box sx={{ mt: 1 }}>
        <Typography variant="body2" color="text.secondary">
          {question}
        </Typography>
        <Box component="ul" sx={{ m: 0, pl: 3, color: 'text.secondary' }}>
          {actions
            .filter((a) => !a.isDecline)
            .map((a, index) => (
              <Typography key={index} component="li" variant="body2">
                {a.label}
              </Typography>
            ))}
        </Box>
      </Box>
    )
  }

  const selected = selectedIndex !== null ? actions[selectedIndex] : null

  const handleSubmit = () => {
    if (!selected || isSubmitting) return
    // The card hands off and reports nothing itself — ChatPage.tsx's onSelect owns the try/catch
    // and feeds isSubmitting/error back down, which is what "never fires and forgets" means here.
    void onSelect(selected)
  }

  return (
    <Paper
      variant="outlined"
      aria-busy={isSubmitting}
      sx={{ mt: 1, p: 1.5, borderRadius: `${radius.md}px`, maxWidth: '75%' }}
    >
      <Chip label="Suggested" size="small" sx={{ mb: 1 }} />
      <Typography id={questionId} variant="body2" sx={{ fontWeight: 500, mb: 1 }}>
        {question}
      </Typography>

      <RadioGroup
        aria-labelledby={questionId}
        value={selectedIndex === null ? '' : String(selectedIndex)}
        onChange={(e) => setSelectedIndex(Number(e.target.value))}
      >
        <Stack spacing={0.25}>
          {actions.map((action, index) => {
            const descriptionId = `${questionId}-desc-${index}`
            return (
              <Box key={index}>
                <FormControlLabel
                  value={String(index)}
                  disabled={isSubmitting}
                  control={<Radio size="small" slotProps={{ input: { 'aria-describedby': descriptionId } }} />}
                  label={
                    <Typography variant="body2" sx={{ fontStyle: action.isDecline ? 'italic' : 'normal' }}>
                      {action.label}
                    </Typography>
                  }
                />
                {action.description && (
                  <Typography id={descriptionId} variant="caption" color="text.secondary" sx={{ display: 'block', pl: 4 }}>
                    {action.description}
                  </Typography>
                )}
              </Box>
            )
          })}
        </Stack>
      </RadioGroup>

      {error && (
        <Alert severity="error" sx={{ mt: 1 }}>
          {error}
        </Alert>
      )}

      <Box sx={{ mt: 1.5, display: 'flex', justifyContent: 'flex-end' }}>
        <Button size="small" variant="contained" disabled={!selected || isSubmitting} onClick={handleSubmit}>
          {isSubmitting ? 'Working…' : 'Choose'}
        </Button>
      </Box>
    </Paper>
  )
}
