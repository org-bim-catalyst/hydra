import { Alert, Box, Button, Link, Stack, Typography } from '@mui/material'
import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { Link as RouterLink } from 'react-router'
import { z } from 'zod'
import { AuthLayout } from '../../../components/AuthLayout'
import { FormField } from '../../../components/FormField'
import { PublicConsentGate } from '../../consent/components/PublicConsentGate'
import { authBranding } from '../../landing/content/copy'
import { flumeriaColor } from '../../landing/theme/flumeriaPalette'
import { useRequestPasswordReset } from '../hooks/usePasswordReset'

interface ForgotPasswordFormValues {
  email: string
}

// Validated with Zod through react-hook-form's `validate` hook rather than a resolver package:
// @hookform/resolvers is not a dependency here, and adding one for a single field is not worth it.
const emailSchema = z.string().trim().min(1, 'Enter your email address.').email('Enter a valid email address.')

export function ForgotPasswordPage() {
  const requestReset = useRequestPasswordReset()
  const [submittedAddress, setSubmittedAddress] = useState<string | null>(null)

  const form = useForm<ForgotPasswordFormValues>({ defaultValues: { email: '' } })

  const onSubmit = form.handleSubmit(async (values) => {
    try {
      await requestReset.mutateAsync(values.email.trim())
      setSubmittedAddress(values.email.trim())
    } catch {
      // requestReset.isError drives the alert below; the user keeps their typed address and can
      // retry. Swallowing this silently would leave them staring at an unchanged form.
    }
  })

  return (
    <PublicConsentGate>
      <AuthLayout
        title="Reset your password"
        subtitle={
          submittedAddress
            ? undefined
            : 'Enter your email address and we will send you a link to choose a new password.'
        }
        tagline={authBranding.signIn.tagline}
        image={authBranding.signIn.image}
      >
        {submittedAddress ? (
          <Stack spacing={3}>
            {/* Deliberately neutral: it never says whether that address has an account. */}
            <Alert severity="success">
              If an account exists for {submittedAddress}, a password reset link is on its way. The
              link expires in one hour and can be used once.
            </Alert>
            <Typography variant="body2" sx={{ color: flumeriaColor.body }}>
              Didn't get it? Check your spam folder, or{' '}
              <Link component="button" type="button" onClick={() => setSubmittedAddress(null)}>
                try a different address
              </Link>
              .
            </Typography>
            <Button component={RouterLink} to="/login" variant="contained" size="large" fullWidth>
              Back to sign in
            </Button>
          </Stack>
        ) : (
          <Box component="form" onSubmit={onSubmit} noValidate>
            <Stack spacing={3}>
              {requestReset.isError && (
                <Alert severity="error">
                  We couldn't send that request. Check your connection and try again in a moment.
                </Alert>
              )}
              <FormField
                id="forgot-password-email"
                label="Email address"
                type="email"
                placeholder="you@example.com"
                error={Boolean(form.formState.errors.email)}
                helperText={form.formState.errors.email?.message}
                {...form.register('email', {
                  validate: (value) => {
                    const result = emailSchema.safeParse(value)
                    return result.success || result.error.issues[0].message
                  },
                })}
              />
              <Button
                type="submit"
                variant="contained"
                size="large"
                fullWidth
                disabled={requestReset.isPending}
              >
                Send reset link
              </Button>
              <Typography variant="body2" sx={{ textAlign: 'center', color: flumeriaColor.body }}>
                Remembered it?{' '}
                <Link component={RouterLink} to="/login">
                  Back to sign in
                </Link>
              </Typography>
            </Stack>
          </Box>
        )}
      </AuthLayout>
    </PublicConsentGate>
  )
}
