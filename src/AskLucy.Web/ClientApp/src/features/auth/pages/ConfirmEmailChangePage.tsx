import { Alert, CircularProgress, Link, Stack, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { AuthLayout } from '../../../components/AuthLayout'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { useConfirmEmailChange } from '../hooks/useAuth'

export function ConfirmEmailChangePage() {
  const [searchParams] = useSearchParams()
  const confirmEmailChange = useConfirmEmailChange()
  const requested = useRef(false)

  const userId = searchParams.get('userId')
  const newEmail = searchParams.get('newEmail')
  const token = searchParams.get('token')

  useEffect(() => {
    if (requested.current || !userId || !newEmail || !token) return
    requested.current = true
    confirmEmailChange.mutate({ userId, newEmail, token })
  }, [userId, newEmail, token, confirmEmailChange])

  return (
    <PublicConsentGate>
      <AuthLayout title="Confirm your new email">
        <Stack spacing={2.5} sx={{ alignItems: 'center' }}>
          {!userId || !newEmail || !token ? (
            <Alert severity="error" sx={{ width: '100%' }}>
              This confirmation link is missing required information.
            </Alert>
          ) : confirmEmailChange.isPending || confirmEmailChange.isIdle ? (
            <CircularProgress size={32} />
          ) : confirmEmailChange.isSuccess ? (
            <Alert severity="success" sx={{ width: '100%' }}>
              Your email has been updated to {newEmail}.
            </Alert>
          ) : (
            <Alert severity="error" sx={{ width: '100%' }}>
              This confirmation link is invalid or has expired.
            </Alert>
          )}
          <Typography variant="body2" color="text.secondary">
            <Link component={RouterLink} to="/login">
              Back to sign in
            </Link>
          </Typography>
        </Stack>
      </AuthLayout>
    </PublicConsentGate>
  )
}
