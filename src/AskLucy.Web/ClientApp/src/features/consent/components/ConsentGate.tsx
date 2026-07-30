import { Alert, Box, Button, CircularProgress } from '@mui/material'
import type { PropsWithChildren } from 'react'
import { useCookieConsent } from '../hooks/useCookieConsent'
import { CookieConsentBanner } from './CookieConsentBanner'

/**
 * Gates the authenticated app shell on the caller's cookie-consent status
 * (specs/004-cookie-consent-privacy). No page renders interactively before this resolves
 * (FR-019/FR-020): while loading, a full-page blocking state is shown instead of the app;
 * once resolved, the app renders normally with `CookieConsentBanner` on top of it as a
 * blocking overlay whenever a decision is required.
 */
export function ConsentGate({ children }: PropsWithChildren) {
  const { data, isPending, isError, refetch } = useCookieConsent()

  if (isPending) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', height: '100vh' }}>
        <CircularProgress role="status" aria-live="polite" aria-label="Loading…" />
      </Box>
    )
  }

  if (isError) {
    return (
      <Box sx={{ display: 'flex', flexDirection: 'column', alignItems: 'center', justifyContent: 'center', height: '100vh', gap: 2 }}>
        <Alert severity="error" role="alert">
          Couldn't load your cookie preferences. Please try again.
        </Alert>
        <Button variant="outlined" onClick={() => void refetch()}>
          Retry
        </Button>
      </Box>
    )
  }

  return (
    <>
      {children}
      {data.requiresReconsent && <CookieConsentBanner />}
    </>
  )
}
