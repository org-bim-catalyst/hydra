import { Alert, Box, Button, Link, Stack, TextField, Typography } from '@mui/material'
import { useForm } from 'react-hook-form'
import { Link as RouterLink } from 'react-router'
import { AuthLayout } from '../../../components/AuthLayout'
import { useRegister } from '../hooks/useAuth'

interface RegisterFormValues {
  email: string
  password: string
  firstName?: string
  lastName?: string
}

export function RegisterPage() {
  const register = useRegister()
  const form = useForm<RegisterFormValues>()

  const onSubmit = form.handleSubmit((values) => register.mutate(values))

  return (
    <AuthLayout eyebrow="Get started" title="Create your account">
      {register.isSuccess ? (
        <Alert severity="success">Check your email to confirm your account.</Alert>
      ) : (
        <Box component="form" onSubmit={onSubmit}>
          <Stack spacing={2.5}>
            {register.isError && <Alert severity="error">Registration failed. Please try again.</Alert>}
            <Stack direction="row" spacing={1.5}>
              <TextField label="First name" fullWidth {...form.register('firstName')} />
              <TextField label="Last name" fullWidth {...form.register('lastName')} />
            </Stack>
            <TextField label="Email" type="email" fullWidth {...form.register('email', { required: true })} />
            <TextField
              label="Password"
              type="password"
              fullWidth
              helperText="At least 8 characters"
              {...form.register('password', { required: true, minLength: 8 })}
            />
            <Button type="submit" variant="contained" size="large" fullWidth disabled={register.isPending}>
              Create account
            </Button>
            <Typography variant="body2" color="text.secondary" sx={{ textAlign: 'center' }}>
              Already have an account? <Link component={RouterLink} to="/login">Sign in</Link>
            </Typography>
          </Stack>
        </Box>
      )}
    </AuthLayout>
  )
}
