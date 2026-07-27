import { Alert, Box, Button, Paper, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
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
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', p: 2 }}>
      <Paper sx={{ p: 4, maxWidth: 400, width: '100%' }}>
        <Typography variant="h5" sx={{ mb: 3 }}>
          Sign in to Ask Lucy
        </Typography>

        {!pendingUserId ? (
          <Box component="form" onSubmit={onSubmitLogin}>
            <Stack spacing={2}>
              {login.isError && <Alert severity="error">Invalid email or password.</Alert>}
              <TextField
                label="Email"
                type="email"
                fullWidth
                {...loginForm.register('email', { required: true })}
              />
              <TextField
                label="Password"
                type="password"
                fullWidth
                {...loginForm.register('password', { required: true })}
              />
              <Button type="submit" variant="contained" disabled={login.isPending}>
                Sign in
              </Button>
              <Stack direction="row" spacing={1}>
                <Button fullWidth variant="outlined" href="/api/v1/auth/external/google">
                  Google
                </Button>
                <Button fullWidth variant="outlined" href="/api/v1/auth/external/facebook">
                  Facebook
                </Button>
              </Stack>
            </Stack>
          </Box>
        ) : (
          <Box component="form" onSubmit={onSubmitTwoFactor}>
            <Stack spacing={2}>
              <Typography>Enter your authenticator app code.</Typography>
              {loginTwoFactor.isError && <Alert severity="error">Invalid code.</Alert>}
              <TextField label="Code" fullWidth {...twoFactorForm.register('code', { required: true })} />
              <Button type="submit" variant="contained" disabled={loginTwoFactor.isPending}>
                Verify
              </Button>
            </Stack>
          </Box>
        )}
      </Paper>
    </Box>
  )
}
