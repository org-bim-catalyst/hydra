import { Alert, CircularProgress, Link, Stack, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { AuthLayout } from '../../../components/AuthLayout'
import { useConfirmEmail } from '../hooks/useAuth'

export function ConfirmEmailPage() {
  const [searchParams] = useSearchParams()
  const confirmEmail = useConfirmEmail()
  const requested = useRef(false)

  const userId = searchParams.get('userId')
  const token = searchParams.get('token')

  useEffect(() => {
    if (requested.current || !userId || !token) return
    requested.current = true
    confirmEmail.mutate({ userId, token })
  }, [userId, token, confirmEmail])

  return (
    <AuthLayout title="Confirm your email">
      <Stack spacing={2.5} sx={{ alignItems: 'center' }}>
        {!userId || !token ? (
          <Alert severity="error" sx={{ width: '100%' }}>
            This confirmation link is missing required information.
          </Alert>
        ) : confirmEmail.isPending || confirmEmail.isIdle ? (
          <CircularProgress size={32} />
        ) : confirmEmail.isSuccess ? (
          <Alert severity="success" sx={{ width: '100%' }}>
            Your email is confirmed. You can now sign in.
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
  )
}
