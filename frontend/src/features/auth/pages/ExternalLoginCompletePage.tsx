import { Alert, CircularProgress, Link, Stack, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router'
import { AuthLayout } from '../../../components/AuthLayout'
import { useCompleteExternalLogin } from '../hooks/useAuth'

/**
 * Landing page for the OAuth redirect flow (FR-010 sign-in, FR-034 link) — the backend never
 * puts a token in this URL, only a one-time completion `code` (or an `error`), which is
 * exchanged here via an authenticated-by-possession-of-the-code POST, not a bearer token.
 */
export function ExternalLoginCompletePage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const completeExternalLogin = useCompleteExternalLogin()
  const requested = useRef(false)

  const code = searchParams.get('code')
  const error = searchParams.get('error')

  useEffect(() => {
    if (requested.current || !code) return
    requested.current = true
    completeExternalLogin.mutate(code, { onSuccess: () => navigate('/chat', { replace: true }) })
  }, [code, completeExternalLogin, navigate])

  return (
    <AuthLayout title="Signing you in">
      <Stack spacing={2.5} sx={{ alignItems: 'center' }}>
        {error || !code ? (
          <Alert severity="error" sx={{ width: '100%' }}>
            That sign-in link is invalid or has expired. Please try again.
          </Alert>
        ) : completeExternalLogin.isError ? (
          <Alert severity="error" sx={{ width: '100%' }}>
            We couldn't complete sign-in. Please try again.
          </Alert>
        ) : (
          <CircularProgress size={32} />
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
