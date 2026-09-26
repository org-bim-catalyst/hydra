import { Alert, Button, Snackbar } from '@mui/material'
import type { TransitionFeedback } from './transitionFeedback'

/** The toast every triage action ends in; a reopen collision links to the newer incident. */
export function TransitionFeedbackSnackbar({
  feedback,
  onClose,
  onOpenIncident,
}: {
  feedback: TransitionFeedback | null
  onClose: () => void
  onOpenIncident: (id: string) => void
}) {
  return (
    <Snackbar open={feedback !== null} autoHideDuration={8000} onClose={onClose}>
      <Alert
        severity={feedback?.severity ?? 'success'}
        variant="filled"
        onClose={onClose}
        action={
          feedback?.newerIncidentId ? (
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                onOpenIncident(feedback.newerIncidentId!)
                onClose()
              }}
            >
              Open it
            </Button>
          ) : undefined
        }
      >
        {feedback?.text}
      </Alert>
    </Snackbar>
  )
}
