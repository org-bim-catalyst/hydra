import { useMutation, useQuery } from '@tanstack/react-query'
import * as authApi from '../api/authApi'

/**
 * Validates the link the reset page was opened with, so an already-used or expired one is
 * reported on arrival rather than after the user has chosen and confirmed a password.
 *
 * `retry: false` matters: a rejected link is a 400, and retrying it would only delay the message.
 */
export function useValidateResetToken(userId: string | null, token: string | null) {
  return useQuery({
    queryKey: ['auth', 'reset-token', userId, token],
    queryFn: () => authApi.validateResetToken(userId!, token!).then(() => true),
    enabled: Boolean(userId && token),
    retry: false,
    staleTime: Infinity,
  })
}

/**
 * Requests a reset link (specs/058-password-recovery US1).
 *
 * There is no `onSuccess` side effect on purpose: success here means "the request was accepted",
 * not "an email is on its way to an account that exists", and the UI must not imply the latter.
 * Errors are left on the mutation for the page to surface (FR-017).
 */
export function useRequestPasswordReset() {
  return useMutation({
    mutationFn: (email: string) => authApi.requestPasswordReset(email),
  })
}

/**
 * Redeems a reset link (specs/058-password-recovery US2). No session results — the API never
 * returns tokens, so the user signs in afresh and any two-factor enrolment still applies (FR-012).
 */
export function useResetPassword() {
  return useMutation({
    mutationFn: ({ userId, token, newPassword }: { userId: string; token: string; newPassword: string }) =>
      authApi.resetPassword(userId, token, newPassword),
  })
}
