import { Alert, Box, Button, Paper, Stack, TextField, Typography } from '@mui/material'
import { useForm } from 'react-hook-form'
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
    <Box sx={{ display: 'flex', justifyContent: 'center', alignItems: 'center', minHeight: '100vh', p: 2 }}>
      <Paper sx={{ p: 4, maxWidth: 400, width: '100%' }}>
        <Typography variant="h5" sx={{ mb: 3 }}>
          Create your Ask Lucy account
        </Typography>

        {register.isSuccess ? (
          <Alert severity="success">Check your email to confirm your account.</Alert>
        ) : (
          <Box component="form" onSubmit={onSubmit}>
            <Stack spacing={2}>
              {register.isError && <Alert severity="error">Registration failed. Please try again.</Alert>}
              <TextField label="First name" fullWidth {...form.register('firstName')} />
              <TextField label="Last name" fullWidth {...form.register('lastName')} />
              <TextField label="Email" type="email" fullWidth {...form.register('email', { required: true })} />
              <TextField
                label="Password"
                type="password"
                fullWidth
                {...form.register('password', { required: true, minLength: 8 })}
              />
              <Button type="submit" variant="contained" disabled={register.isPending}>
                Create account
              </Button>
            </Stack>
          </Box>
        )}
      </Paper>
    </Box>
  )
}
