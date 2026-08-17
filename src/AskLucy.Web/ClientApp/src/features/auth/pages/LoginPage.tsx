import { Alert, Box, Button, Divider, Link, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink, useNavigate } from 'react-router'
import { API_BASE_URL } from '../../../api/httpClient'
import { AuthLayout } from '../../../components/AuthLayout'
import { FormField } from '../../../components/FormField'
import { FacebookGlyph, GoogleGlyph } from '../../../components/OAuthGlyphs'
import { useFunnelAnalytics } from '../../analytics/hooks/useFunnelAnalytics'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { authBranding } from '../../landing/content/copy'
import { flumeriaColor } from '../../landing/theme/flumeriaPalette'
import { useLogin, useLoginTwoFactor } from '../hooks/useAuth'

interface LoginFormValues {
  email: string
  password: string
}

interface TwoFactorFormValues {
  code: string
}

export function LoginPage() {
  const navigate = useNavigate()
  const login = useLogin()
  const loginTwoFactor = useLoginTwoFactor()
  const { recordFunnelCompleted } = useFunnelAnalytics()
  const [pendingUserId, setPendingUserId] = useState<string | null>(null)

  const loginForm = useForm<LoginFormValues>()
  const twoFactorForm = useForm<TwoFactorFormValues>()

  const onSubmitLogin = loginForm.handleSubmit(async (values) => {
    const result = await login.mutateAsync(values)
    if (result.requiresTwoFactor && result.userId) {
      setPendingUserId(result.userId)
    } else {
      // FR-021: fired immediately before navigating, never delaying it (contracts/
      // routing-and-consent-contract.md).
      recordFunnelCompleted('SignIn')
      navigate('/studio')
    }
  })

  const onSubmitTwoFactor = twoFactorForm.handleSubmit(async (values) => {
    if (!pendingUserId) return
    await loginTwoFactor.mutateAsync({
      userId: pendingUserId,
      code: values.code,
      isRecoveryCode: false,
    })
    recordFunnelCompleted('SignIn')
    navigate('/studio')
  })

  return (
    <PublicConsentGate>
      <AuthLayout
        title={pendingUserId ? 'Verify your identity' : 'Welcome back'}
        subtitle={pendingUserId ? undefined : authBranding.signIn.subtitle}
        tagline={authBranding.signIn.tagline}
        image={authBranding.signIn.image}
      >
        {!pendingUserId ? (
          <Box component="form" onSubmit={onSubmitLogin}>
            <Stack spacing={3}>
              {login.isError && <Alert severity="error">Invalid email or password.</Alert>}
              <FormField
                id="login-email"
                label="Email address"
                type="email"
                placeholder="you@example.com"
                {...loginForm.register('email', { required: true })}
              />
              <FormField
                id="login-password"
                label="Password"
                type="password"
                placeholder="Enter your password"
                {...loginForm.register('password', { required: true })}
              />
              <Button
                type="submit"
                variant="contained"
                size="large"
                fullWidth
                disabled={login.isPending}
              >
                Sign In
              </Button>
              <Divider sx={{ color: flumeriaColor.body, fontSize: '0.875rem' }}>or</Divider>
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
                Don't have an account?{' '}
                <Link component={RouterLink} to="/register">
                  Sign up
                </Link>
              </Typography>
            </Stack>
          </Box>
        ) : (
          <Box component="form" onSubmit={onSubmitTwoFactor}>
            <Stack spacing={3}>
              <Typography variant="body2" sx={{ color: flumeriaColor.body }}>
                Enter the code from your authenticator app.
              </Typography>
              {loginTwoFactor.isError && <Alert severity="error">Invalid code.</Alert>}
              <TextField
                label="Code"
                fullWidth
                {...twoFactorForm.register('code', { required: true })}
              />
              <Button
                type="submit"
                variant="contained"
                size="large"
                fullWidth
                disabled={loginTwoFactor.isPending}
              >
                Verify
              </Button>
            </Stack>
          </Box>
        )}
      </AuthLayout>
    </PublicConsentGate>
  )
}
