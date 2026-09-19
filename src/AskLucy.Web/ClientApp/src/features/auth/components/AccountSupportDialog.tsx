import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Stack,
  TextField,
} from '@mui/material'
import { useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import { useRequestAccountSupport } from '../hooks/useAuth'

interface AccountSupportDialogProps {
  open: boolean
  onClose: () => void
  /** Pre-filled from the sign-in form so a locked-out user does not retype it. */
  email: string
}

/**
 * Lets a locked-out user reach an administrator from the sign-in page.
 *
 * The administrator's address is deliberately absent: the server holds it (`Smtp:FromSupport`)
 * and the endpoint returns nothing about it, so the page can offer the route without publishing
 * a mailbox for anyone who visits /login to harvest.
 */
export function AccountSupportDialog({ open, onClose, email }: AccountSupportDialogProps) {
  const [message, setMessage] = useState('')
  const support = useRequestAccountSupport()

  const handleClose = () => {
    // Reset so reopening does not show the previous outcome, and a failed send does not leave a
    // stale error behind the next attempt.
    support.reset()
    setMessage('')
    onClose()
  }

  const onSubmit = async (event: React.FormEvent) => {
    event.preventDefault()
    try {
      await support.mutateAsync({ email, message })
    } catch {
      // `support.isError` renders the alert below; nothing is swallowed.
    }
  }

  const errorDetail = support.error instanceof ApiError ? support.error.detail : null

  return (
    <Dialog open={open} onClose={handleClose} fullWidth maxWidth="sm">
      <DialogTitle>Contact an administrator</DialogTitle>
      {support.isSuccess ? (
        <>
          <DialogContent>
            <Alert severity="success">
              Your message has been sent. An administrator will follow up by email at {email}.
            </Alert>
          </DialogContent>
          <DialogActions>
            <Button onClick={handleClose}>Close</Button>
          </DialogActions>
        </>
      ) : (
        <form onSubmit={onSubmit}>
          <DialogContent>
            <Stack spacing={2}>
              <DialogContentText>
                Tell us what happened and an administrator will get back to you at {email}.
              </DialogContentText>
              {support.isError && (
                <Alert severity="error">
                  {errorDetail ?? "We couldn't send your message. Please try again in a moment."}
                </Alert>
              )}
              <TextField
                label="Message"
                multiline
                minRows={4}
                fullWidth
                required
                // Matches the server-side FluentValidation bound on RequestAccountSupportCommand.
                slotProps={{ htmlInput: { maxLength: 2000 } }}
                value={message}
                onChange={(event) => setMessage(event.target.value)}
              />
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={handleClose}>Cancel</Button>
            <Button type="submit" variant="contained" disabled={support.isPending || message.trim().length === 0}>
              Send message
            </Button>
          </DialogActions>
        </form>
      )}
    </Dialog>
  )
}
