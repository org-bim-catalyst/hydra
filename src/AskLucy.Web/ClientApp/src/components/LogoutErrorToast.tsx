import { Alert, Snackbar } from '@mui/material'
import { useMutationState } from '@tanstack/react-query'
import { useState } from 'react'
import { LOGOUT_MUTATION_KEY } from '../features/auth/hooks/useAuth'
import { useT } from '../i18n/useT'

/**
 * The sign-out that reached this tab but not the server. Mounted at the app root rather than beside
 * `UserMenu`: the sign-out navigates out of the app, so the menu (and every shell around it) is gone
 * by the time the failure is known.
 *
 * Reads the mutation cache so the toast survives that unmount, same pattern as
 * `LanguageSwitchErrorToast`. It carries no retry: the user is already signed out locally, and the
 * server-side refresh token expiring on its own is the only remaining outcome.
 */
export function LogoutErrorToast() {
  const t = useT('notifications')
  const [dismissed, setDismissed] = useState<number | null>(null)
  const attempts = useMutationState({
    filters: { mutationKey: LOGOUT_MUTATION_KEY },
    select: (mutation) => ({
      status: mutation.state.status,
      submittedAt: mutation.state.submittedAt,
    }),
  })
  const latest = attempts.reduce<(typeof attempts)[number] | undefined>(
    (best, attempt) =>
      best === undefined || attempt.submittedAt > best.submittedAt ? attempt : best,
    undefined,
  )
  const failed = latest?.status === 'error' && latest.submittedAt !== dismissed

  const dismiss = () => {
    if (latest) setDismissed(latest.submittedAt)
  }

  return (
    <Snackbar
      open={failed}
      autoHideDuration={15000}
      onClose={(_, reason) => {
        if (reason !== 'clickaway') dismiss()
      }}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
    >
      <Alert severity="error" variant="filled" onClose={dismiss}>
        {t('session.signOutFailed')}
      </Alert>
    </Snackbar>
  )
}
