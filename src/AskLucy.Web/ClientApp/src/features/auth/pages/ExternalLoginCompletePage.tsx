import { Alert, CircularProgress, Link, Stack, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import { Link as RouterLink, useNavigate, useSearchParams } from 'react-router'
import { AuthLayout } from '../../../components/AuthLayout'
import { useFunnelAnalytics } from '../../analytics/hooks/useFunnelAnalytics'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { useCompleteExternalLogin } from '../hooks/useAuth'

/**
 * Landing page for the OAuth redirect flow (FR-010 sign-in, FR-034 link) — the backend never
 * puts a token in this URL, only a one-time completion `code` (or an `error`), which is
 * exchanged here via an authenticated-by-possession-of-the-code POST, not a bearer token.
 *
 * The exchange + redirect are driven by a plain async/await inside the effect (via
 * `mutateAsync`), not a `.mutate()` callback or a separate effect watching `isSuccess`.
 * Both of those alternatives depend on this component re-rendering (or on a specific
 * mutation-observer instance) after the request settles, which React 19 StrictMode's
 * dev-only double-invocation of this component can decouple — the mutation settles
 * correctly (confirmed: session gets saved) but the redirect never fires. Awaiting the
 * promise directly sidesteps that entirely: the redirect decision is made from the local
 * `await` result, never from watching component/render state.
 */
export function ExternalLoginCompletePage() {
  const [searchParams] = useSearchParams()
  const navigate = useNavigate()
  const completeExternalLogin = useCompleteExternalLogin()
  const { recordFunnelCompleted } = useFunnelAnalytics()
  const requested = useRef(false)

  const code = searchParams.get('code')
  const error = searchParams.get('error')

  useEffect(() => {
    if (requested.current || !code) return
    requested.current = true

    completeExternalLogin
      .mutateAsync(code)
      .then(() => {
        // FR-021: a social-login round-trip is a sign-in funnel completion too.
        recordFunnelCompleted('SignIn')
        navigate('/chat', { replace: true })
      })
      .catch(() => {
        // completeExternalLogin.isError drives the error alert below; nothing further to do.
      })
    // eslint-disable-next-line react-hooks/exhaustive-deps -- mutateAsync/navigate/recordFunnelCompleted are stable; re-running on their identity would defeat the one-shot `requested` guard.
  }, [code])

  return (
    <PublicConsentGate>
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
    </PublicConsentGate>
  )
}
