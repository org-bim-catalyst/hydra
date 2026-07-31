import { Alert, Box, Button, Link, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink, useNavigate } from 'react-router'
import { API_BASE_URL } from '../../../api/httpClient'
import { AuthLayout } from '../../../components/AuthLayout'
import { DimensionDivider } from '../../../components/DimensionDivider'
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
  const [pendingUserId, setPendingUserId] = useState<string | null>(null)

  const loginForm = useForm<LoginFormValues>()
  const twoFactorForm = useForm<TwoFactorFormValues>()

  const onSubmitLogin = loginForm.handleSubmit(async (values) => {
    const result = await login.mutateAsync(values)
    if (result.requiresTwoFactor && result.userId) {
      setPendingUserId(result.userId)
    } else {
      navigate('/chat')
    }
  })

  const onSubmitTwoFactor = twoFactorForm.handleSubmit(async (values) => {
    if (!pendingUserId) return
    await loginTwoFactor.mutateAsync({ userId: pendingUserId, code: values.code, isRecoveryCode: false })
    navigate('/chat')
  })

  return (
    <AuthLayout
      eyebrow={pendingUserId ? 'Two-factor' : 'Sign in'}
      title={pendingUserId ? 'Verify your identity' : 'Welcome back'}
    >
      {!pendingUserId ? (
        <Box component="form" onSubmit={onSubmitLogin}>
          <Stack spacing={3}>
            {login.isError && <Alert severity="error">Invalid email or password.</Alert>}
            <TextField label="Email" type="email" fullWidth {...loginForm.register('email', { required: true })} />
            <TextField
              label="Password"
              type="password"
              fullWidth
              {...loginForm.register('password', { required: true })}
            />
            <Button
              type="submit"
              variant="contained"
              size="large"
              fullWidth
              disabled={login.isPending}
              sx={{ mt: 0.5 }}
            >
              Sign in
            </Button>
            <DimensionDivider>or continue with</DimensionDivider>
            <Stack direction="row" spacing={1.5} sx={{ width: '100%' }}>
              <Button
                variant="outlined"
                href={`${API_BASE_URL}/auth/external/google/challenge`}
                sx={{ flex: '1 1 0%', minWidth: 0, px: 0 }}
              >
                Google
              </Button>
              <Button
                variant="outlined"
                href={`${API_BASE_URL}/auth/external/facebook/challenge`}
                sx={{ flex: '1 1 0%', minWidth: 0, px: 0 }}
              >
                Facebook
              </Button>
            </Stack>
            <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center' }}>
              Don't have an account? <Link component={RouterLink} to="/register">Create one</Link>
            </Typography>
          </Stack>
        </Box>
      ) : (
        <Box component="form" onSubmit={onSubmitTwoFactor}>
          <Stack spacing={3}>
            <Typography variant="body2" color="text.secondary">
              Enter the code from your authenticator app.
            </Typography>
            {loginTwoFactor.isError && <Alert severity="error">Invalid code.</Alert>}
            <TextField label="Code" fullWidth {...twoFactorForm.register('code', { required: true })} />
            <Button type="submit" variant="contained" size="large" fullWidth disabled={loginTwoFactor.isPending}>
              Verify
            </Button>
          </Stack>
        </Box>
      )}
    </AuthLayout>
  )
}
