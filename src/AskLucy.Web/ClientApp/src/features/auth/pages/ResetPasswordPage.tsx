import { Alert, Box, Button, CircularProgress, Link, List, ListItem, Stack, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink, useSearchParams } from 'react-router'
import { ApiError } from '../../../api/httpClient'
import { AuthLayout } from '../../../components/AuthLayout'
import { FormField } from '../../../components/FormField'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { authBranding } from '../../landing/content/copy'
import { flumeriaColor } from '../../landing/theme/flumeriaPalette'
import { PasswordRequirements } from '../components/PasswordRequirements'
import { useResetPassword, useValidateResetToken } from '../hooks/usePasswordReset'
import { isPasswordPolicyMet } from '../passwordPolicy'

interface ResetPasswordFormValues {
  newPassword: string
  confirmPassword: string
}

export function ResetPasswordPage() {
  const [searchParams] = useSearchParams()
  const userId = searchParams.get('userId')
  const token = searchParams.get('token')

  const resetPassword = useResetPassword()
  const linkCheck = useValidateResetToken(userId, token)
  const [done, setDone] = useState(false)

  const form = useForm<ResetPasswordFormValues>({ defaultValues: { newPassword: '', confirmPassword: '' } })
  const newPassword = form.watch('newPassword')

  const error = resetPassword.error instanceof ApiError ? resetPassword.error : null
  const policyErrors = error?.errors ? Object.values(error.errors).flat() : []
  const linkRejected = Boolean(error) && policyErrors.length === 0

  const onSubmit = form.handleSubmit(async (values) => {
    if (!userId || !token) return

    try {
      await resetPassword.mutateAsync({ userId, token, newPassword: values.newPassword })
      setDone(true)
    } catch {
      // Rendered from `resetPassword.error` below — either the per-rule policy failures or the
      // one undifferentiated "link is no longer valid". Nothing is swallowed.
    }
  })

  const layoutProps = {
    tagline: authBranding.signIn.tagline,
    image: authBranding.signIn.image,
  }

  if (!userId || !token) {
    return (
      <PublicConsentGate>
        <AuthLayout title="This link is incomplete" {...layoutProps}>
          <Stack spacing={3}>
            <Alert severity="error">
              This password reset link is missing information. Copy it from your email again, or
              request a new one.
            </Alert>
            <Button component={RouterLink} to="/forgot-password" variant="contained" size="large" fullWidth>
              Request a new link
            </Button>
          </Stack>
        </AuthLayout>
      </PublicConsentGate>
    )
  }

  // A link that has already been used, expired, or been superseded is reported on arrival rather
  // than after the user has chosen and confirmed a password. Reusing a spent link previously
  // rendered the full form with no hint that it could never succeed.
  if (linkCheck.isError) {
    return (
      <PublicConsentGate>
        <AuthLayout title="This link is no longer valid" {...layoutProps}>
          <Stack spacing={3}>
            <Alert severity="error">
              This password reset link has expired, has already been used, or has been replaced by a
              newer one. Request a new link and we'll email you a fresh one.
            </Alert>
            <Button component={RouterLink} to="/forgot-password" variant="contained" size="large" fullWidth>
              Request a new link
            </Button>
            <Typography variant="body2" sx={{ textAlign: 'center', color: flumeriaColor.body }}>
              Remembered your password?{' '}
              <Link component={RouterLink} to="/login">
                Back to sign in
              </Link>
            </Typography>
          </Stack>
        </AuthLayout>
      </PublicConsentGate>
    )
  }

  // Held back rather than flashing a form that may be about to be replaced by the message above.
  if (linkCheck.isPending) {
    return (
      <PublicConsentGate>
        <AuthLayout title="Checking your link" {...layoutProps}>
          <Stack spacing={3} sx={{ alignItems: 'center' }}>
            <CircularProgress aria-label="Checking your password reset link" />
            <Typography variant="body2" sx={{ color: flumeriaColor.body }}>
              One moment while we check this password reset link.
            </Typography>
          </Stack>
        </AuthLayout>
      </PublicConsentGate>
    )
  }

  if (done) {
    return (
      <PublicConsentGate>
        <AuthLayout title="Password updated" {...layoutProps}>
          <Stack spacing={3}>
            <Alert severity="success">
              Your password has been changed and every device has been signed out. Sign in with your
              new password to continue.
            </Alert>
            <Button component={RouterLink} to="/login" variant="contained" size="large" fullWidth>
              Go to sign in
            </Button>
          </Stack>
        </AuthLayout>
      </PublicConsentGate>
    )
  }

  return (
    <PublicConsentGate>
      <AuthLayout title="Choose a new password" subtitle="Pick something you have not used here before." {...layoutProps}>
        <Box component="form" onSubmit={onSubmit} noValidate>
          <Stack spacing={3}>
            {linkRejected && (
              <Alert severity="error">
                This password reset link is no longer valid — it may have expired, already been
                used, or been replaced by a newer one.{' '}
                <Link component={RouterLink} to="/forgot-password">
                  Request a new link
                </Link>
                .
              </Alert>
            )}
            {policyErrors.length > 0 && (
              <Alert severity="error">
                That password does not meet the requirements:
                <List dense sx={{ listStyleType: 'disc', pl: 3, py: 0 }}>
                  {policyErrors.map((message) => (
                    <ListItem key={message} sx={{ display: 'list-item', px: 0 }} disableGutters>
                      {message}
                    </ListItem>
                  ))}
                </List>
              </Alert>
            )}
            <Box>
              <FormField
                id="reset-new-password"
                label="New password"
                type="password"
                placeholder="Enter a new password"
                // On the <input>, not the FormControl wrapper — see RegisterPage for why.
                slotProps={{ htmlInput: { 'aria-describedby': 'reset-password-requirements' } }}
                // No helperText: it would restate the checklist rendered directly below.
                error={Boolean(form.formState.errors.newPassword)}
                {...form.register('newPassword', {
                  validate: (value) =>
                    isPasswordPolicyMet(value) || 'Your password does not meet all the requirements yet.',
                })}
              />
              <PasswordRequirements id="reset-password-requirements" password={newPassword} />
            </Box>
            <FormField
              id="reset-confirm-password"
              label="Confirm new password"
              type="password"
              placeholder="Enter it again"
              error={Boolean(form.formState.errors.confirmPassword)}
              helperText={form.formState.errors.confirmPassword?.message}
              {...form.register('confirmPassword', {
                validate: (value) =>
                  value === form.getValues('newPassword') || 'Both passwords must match.',
              })}
            />
            <Button type="submit" variant="contained" size="large" fullWidth disabled={resetPassword.isPending}>
              Set new password
            </Button>
            <Typography variant="body2" sx={{ textAlign: 'center', color: flumeriaColor.body }}>
              Changed your mind?{' '}
              <Link component={RouterLink} to="/login">
                Back to sign in
              </Link>
            </Typography>
          </Stack>
        </Box>
      </AuthLayout>
    </PublicConsentGate>
  )
}
