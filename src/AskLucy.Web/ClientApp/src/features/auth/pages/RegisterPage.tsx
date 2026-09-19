import { Alert, Box, Button, Divider, Link, List, ListItem, Stack, Typography } from '@mui/material'
import { useEffect, useRef } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink } from 'react-router'
import { API_BASE_URL, ApiError } from '../../../api/httpClient'
import { AuthLayout } from '../../../components/AuthLayout'
import { FormField } from '../../../components/FormField'
import { FacebookGlyph, GoogleGlyph } from '../../../components/OAuthGlyphs'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { useFunnelAnalytics } from '../../analytics/hooks/useFunnelAnalytics'
import { authBranding } from '../../landing/content/copy'
import { flumeriaColor } from '../../landing/theme/flumeriaPalette'
import { useRegister } from '../hooks/useAuth'

interface RegisterFormValues {
  email: string
  password: string
  confirmPassword: string
  firstName?: string
  lastName?: string
}

/**
 * Registration does not issue a session or redirect (spec.md FR-008, Clarifications) — the
 * existing, unchanged email-confirmation requirement means a successful sign-up ends in a
 * branded confirmation-pending state, not the workspace. See RegisterCommandHandler.cs.
 */
export function RegisterPage() {
  const register = useRegister()
  const form = useForm<RegisterFormValues>()
  const { recordFunnelCompleted } = useFunnelAnalytics()
  const funnelEventSent = useRef(false)

  // AuthController.Register returns the identity failures ("Passwords must have at least one
  // non-alphanumeric character", "Email 'x' is already taken", ...) in Problem Details. A flat
  // "Registration failed. Please try again." left the user retrying a password the server was
  // never going to accept, so show what the server actually objected to.
  const registerError = register.error instanceof ApiError ? register.error : null
  const registerReasons = registerError?.errors
    ? Object.values(registerError.errors).flat()
    : registerError?.detail
      ? [registerError.detail]
      : []

  const onSubmit = form.handleSubmit(({ email, password, firstName, lastName }) =>
    register.mutate({ email, password, firstName, lastName }),
  )

  useEffect(() => {
    if (register.isSuccess && !funnelEventSent.current) {
      funnelEventSent.current = true
      recordFunnelCompleted('SignUp')
    }
  }, [register.isSuccess, recordFunnelCompleted])

  return (
    <PublicConsentGate>
      <AuthLayout
        title="Create your account"
        subtitle={authBranding.signUp.subtitle}
        tagline={authBranding.signUp.tagline}
        image={authBranding.signUp.image}
      >
        {register.isSuccess ? (
          <Alert severity="success">Check your email to confirm your account.</Alert>
        ) : (
          <Box component="form" onSubmit={onSubmit}>
            <Stack spacing={3}>
              {register.isError && (
                <Alert severity="error">
                  {registerReasons.length > 0 ? (
                    <>
                      We couldn't create your account:
                      <List dense sx={{ listStyleType: 'disc', pl: 3, py: 0 }}>
                        {registerReasons.map((message) => (
                          <ListItem key={message} sx={{ display: 'list-item', px: 0 }} disableGutters>
                            {message}
                          </ListItem>
                        ))}
                      </List>
                    </>
                  ) : (
                    'Registration failed. Please try again.'
                  )}
                </Alert>
              )}
              <Stack direction="row" spacing={1.5}>
                <FormField id="register-first-name" label="First name" {...form.register('firstName')} />
                <FormField id="register-last-name" label="Last name" {...form.register('lastName')} />
              </Stack>
              <FormField
                id="register-email"
                label="Email address"
                type="email"
                placeholder="you@example.com"
                {...form.register('email', { required: true })}
              />
              <FormField
                id="register-password"
                label="Password"
                type="password"
                placeholder="Min. 8 characters"
                // The authoritative policy is ASP.NET Identity's (Persistence/DependencyInjection.cs)
                // and any rejection comes back in the alert above; this is the up-front hint, which
                // previously advertised only the length rule and let people submit passwords the
                // server was always going to refuse.
                helperText="At least 8 characters, with an uppercase letter, a lowercase letter, a number and a symbol."
                {...form.register('password', { required: true, minLength: 8 })}
              />
              <FormField
                id="register-confirm-password"
                label="Confirm password"
                type="password"
                placeholder="Re-enter your password"
                error={!!form.formState.errors.confirmPassword}
                helperText={form.formState.errors.confirmPassword?.message}
                {...form.register('confirmPassword', {
                  required: true,
                  validate: (value) => value === form.getValues('password') || "Passwords don't match",
                })}
              />
              <Button
                type="submit"
                variant="contained"
                size="large"
                fullWidth
                disabled={register.isPending}
              >
                Create Account
              </Button>
              <Divider sx={{ color: flumeriaColor.body, fontSize: '0.875rem' }}>or sign up with</Divider>
              <Stack spacing={1.5} sx={{ width: '100%' }}>
                <Button
                  variant="outlined"
                  href={`${API_BASE_URL}/auth/external/google/challenge`}
                  startIcon={<GoogleGlyph />}
                  fullWidth
                >
                  Continue with Google
                </Button>
                <Button
                  variant="outlined"
                  href={`${API_BASE_URL}/auth/external/facebook/challenge`}
                  startIcon={<FacebookGlyph />}
                  fullWidth
                >
                  Continue with Facebook
                </Button>
              </Stack>
              <Typography variant="body2" sx={{ textAlign: 'center', color: flumeriaColor.body }}>
                Already have an account?{' '}
                <Link component={RouterLink} to="/login">
                  Sign in
                </Link>
              </Typography>
            </Stack>
          </Box>
        )}
      </AuthLayout>
    </PublicConsentGate>
  )
}
