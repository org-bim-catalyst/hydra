import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as authApi from '../api/authApi'
import type { SessionResponse } from '../api/authApi'
import { useAuthStore } from '../../../store/authStore'
import { SESSION_QUERY_KEY } from './useSession'

const LOGGED_OUT_SESSION: SessionResponse = { authenticated: false, userId: null, roles: [], permissions: [] }

const EXTERNAL_LOGINS_QUERY_KEY = ['auth', 'external-logins']

const PASSWORD_STATUS_QUERY_KEY = ['auth', 'password-status']

/**
 * Returned (not `void`ed) from the sign-in mutations' `onSuccess` so react-query awaits it and
 * `mutateAsync` only resolves once the session query holds the *new* session.
 *
 * Signing in from `/login` leaves a cached 401 `ApiError` in `SESSION_QUERY_KEY` with a 60s
 * `staleTime`. Firing the refresh without awaiting it meant the caller's `navigate('/studio')`
 * ran first, ProtectedRoute read that stale error, and redirected straight back to `/login` —
 * the "first click does nothing, second click works" bug, where the second click only worked
 * because the background refetch had landed by then.
 *
 * `refetchQueries` rather than `invalidateQueries`: invalidation only refetches *active*
 * queries by default, and nothing on the auth pages observes the session query.
 */
function refreshSession(queryClient: ReturnType<typeof useQueryClient>) {
  return queryClient.refetchQueries({ queryKey: SESSION_QUERY_KEY })
}

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ email, password }: { email: string; password: string }) => authApi.login(email, password),
    onSuccess: (result) => {
      if (!result.requiresTwoFactor && result.accessToken && result.userId) {
        setSession(result.accessToken, result.userId)
        return refreshSession(queryClient)
      }
    },
  })
}

export function useLoginTwoFactor() {
  const setSession = useAuthStore((s) => s.setSession)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ userId, code, isRecoveryCode }: { userId: string; code: string; isRecoveryCode: boolean }) =>
      authApi.loginTwoFactor(userId, code, isRecoveryCode),
    onSuccess: (result) => {
      if (result.accessToken && result.userId) {
        setSession(result.accessToken, result.userId)
        return refreshSession(queryClient)
      }
    },
  })
}

export function useRegister() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({
      email,
      password,
      firstName,
      lastName,
    }: {
      email: string
      password: string
      firstName?: string
      lastName?: string
    }) => authApi.register(email, password, firstName, lastName),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY }),
  })
}

/**
 * Re-sends the account-confirmation link, offered on the sign-in page when a sign-in is refused
 * because the address was never confirmed.
 */
export function useResendEmailConfirmation() {
  return useMutation({ mutationFn: (email: string) => authApi.resendEmailConfirmation(email) })
}

/** Sends a locked-out user's message to support. The destination address never reaches the client. */
export function useRequestAccountSupport() {
  return useMutation({
    mutationFn: ({ email, message }: { email: string; message: string }) =>
      authApi.requestAccountSupport(email, message),
  })
}

export function useLogout() {
  const clear = useAuthStore((s) => s.clear)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: () => authApi.logout(),
    onSettled: () => {
      clear()
      // Set the cache synchronously rather than only invalidating — invalidateQueries schedules
      // a background refetch, leaving `data` at its stale "authenticated: true" value for that
      // request's round trip. In that window ProtectedRoute still renders ConsentGate (reading
      // this same query), which fires its own now-unauthorized request and surfaces its "Couldn't
      // load your cookie preferences" error — even though the URL has already changed to /login.
      queryClient.setQueryData(SESSION_QUERY_KEY, LOGGED_OUT_SESSION)
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY })
    },
  })
}

export function useConfirmEmail() {
  return useMutation({
    mutationFn: ({ userId, token }: { userId: string; token: string }) => authApi.confirmEmail(userId, token),
  })
}

export function useChangePassword() {
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ currentPassword, newPassword }: { currentPassword?: string; newPassword: string }) =>
      authApi.changePassword(currentPassword, newPassword),
    // An account that had no password now has one, so the form must stop offering "Set password".
    onSuccess: () => queryClient.invalidateQueries({ queryKey: PASSWORD_STATUS_QUERY_KEY }),
  })
}

/**
 * specs/058-password-recovery US4. Drives which password form Settings shows; an external-only
 * account has no current password to ask for.
 */
export function usePasswordStatus() {
  return useQuery({
    queryKey: PASSWORD_STATUS_QUERY_KEY,
    queryFn: () => authApi.getPasswordStatus(),
  })
}

export function useRequestEmailChange() {
  return useMutation({
    mutationFn: (newEmail: string) => authApi.requestEmailChange(newEmail),
  })
}

export function useConfirmEmailChange() {
  return useMutation({
    mutationFn: ({ userId, newEmail, token }: { userId: string; newEmail: string; token: string }) =>
      authApi.confirmEmailChange(userId, newEmail, token),
  })
}

export function useExternalLogins() {
  return useQuery({ queryKey: EXTERNAL_LOGINS_QUERY_KEY, queryFn: authApi.getExternalLogins })
}

export function useCompleteExternalLogin() {
  const setSession = useAuthStore((s) => s.setSession)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: (code: string) => authApi.completeExternalLogin(code),
    onSuccess: (result) => {
      if (result.accessToken && result.userId) {
        setSession(result.accessToken, result.userId)
        return refreshSession(queryClient)
      }
    },
  })
}

export function useIssueExternalLoginLinkTicket() {
  return useMutation({ mutationFn: authApi.issueExternalLoginLinkTicket })
}

export function useRemoveExternalLogin() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ provider, providerKey }: { provider: string; providerKey: string }) =>
      authApi.removeExternalLogin(provider, providerKey),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: EXTERNAL_LOGINS_QUERY_KEY }),
  })
}

export function useEnableTwoFactor() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: authApi.enableTwoFactor,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['profile', 'me'] }),
  })
}

export function useDisableTwoFactor() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: authApi.disableTwoFactor,
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['profile', 'me'] }),
  })
}

export function useGenerateRecoveryCodes() {
  return useMutation({ mutationFn: authApi.generateRecoveryCodes })
}
