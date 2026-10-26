import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as authApi from '../api/authApi'
import type { SessionResponse } from '../api/authApi'
import { migratePublicConsentToAccount } from '../../consent/hooks/usePublicCookieConsent'
import { useAuthStore } from '../../../store/authStore'
import { useActiveSiteBoundaryStore } from '../../../store/activeSiteBoundaryStore'
import { useActiveConversationStore } from '../../chat/activeConversationStore'
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

/**
 * Best-effort: a failed promotion just means ConsentGate falls back to prompting the user
 * again post-login, same as before this existed — it must never fail the sign-in itself.
 */
function migrateConsent(queryClient: ReturnType<typeof useQueryClient>) {
  return migratePublicConsentToAccount(queryClient).catch(() => undefined)
}

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession)
  const queryClient = useQueryClient()

  return useMutation({
    mutationFn: ({ email, password }: { email: string; password: string }) => authApi.login(email, password),
    onSuccess: (result) => {
      if (!result.requiresTwoFactor && result.accessToken && result.userId) {
        setSession(result.accessToken, result.userId)
        return Promise.all([refreshSession(queryClient), migrateConsent(queryClient)])
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
        return Promise.all([refreshSession(queryClient), migrateConsent(queryClient)])
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

export const LOGOUT_MUTATION_KEY = ['auth', 'logout'] as const

export function useLogout() {
  const clear = useAuthStore((s) => s.clear)
  const queryClient = useQueryClient()

  return useMutation({
    mutationKey: LOGOUT_MUTATION_KEY,
    mutationFn: () => authApi.logout(),
    // Settling, not succeeding: signing out is a local act. A server that refuses or never receives
    // the logout must not leave the user inside an app they have already been cleared out of.
    onSettled: () => {
      clear()
      // Everything the signed-in account left behind goes too, so the next account starts from nothing: the
      // open conversation id (persisted in sessionStorage, so another account would load it and fail), the
      // site outline on the map, and every cached query (chats, messages, boundaries, profile).
      useActiveConversationStore.getState().setActiveChatId(null)
      useActiveSiteBoundaryStore.getState().clearBoundary()
      // Set the session synchronously rather than only invalidating — invalidateQueries schedules
      // a background refetch, leaving `data` at its stale "authenticated: true" value for that
      // request's round trip. In that window ProtectedRoute still renders ConsentGate (reading
      // this same query), which fires its own now-unauthorized request and surfaces its "Couldn't
      // load your cookie preferences" error — even though the URL has already changed to /login.
      //
      // Order matters: the session entry is updated while the mounted guards still hold the live
      // query, and only then is the rest of the cache evicted. `queryClient.clear()` here would take
      // the session query away too, and a mounted ProtectedRoute keeps reading the removed query
      // object — still "authenticated: true" — so nothing redirects and the sign-out leaves the page
      // open under a header that already says "Sign in".
      queryClient.setQueryData(SESSION_QUERY_KEY, LOGGED_OUT_SESSION)
      const sessionQuery = queryClient.getQueryCache().find({ queryKey: SESSION_QUERY_KEY })
      queryClient.removeQueries({ predicate: (query) => query !== sessionQuery })
      // The mutation cache used to go with `clear()`; keep the account's other attempts (and the
      // variables they carry) out of the next session's keyed toasts, except the sign-out itself —
      // `LogoutErrorToast` reads this one to report a logout the server refused.
      const signOuts = new Set(queryClient.getMutationCache().findAll({ mutationKey: LOGOUT_MUTATION_KEY }))
      for (const mutation of queryClient.getMutationCache().getAll()) {
        if (!signOuts.has(mutation)) queryClient.getMutationCache().remove(mutation)
      }
      // Deliberately not refetched: a logout the server refused or never received leaves the refresh
      // cookie valid, so a refetch would answer "authenticated: true" and pull the user back into the
      // page they just signed out of. The local session is gone either way.
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
        return Promise.all([refreshSession(queryClient), migrateConsent(queryClient)])
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
