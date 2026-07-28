import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import * as authApi from '../api/authApi'
import { useAuthStore } from '../../../store/authStore'

const EXTERNAL_LOGINS_QUERY_KEY = ['auth', 'external-logins']

export function useLogin() {
  const setSession = useAuthStore((s) => s.setSession)

  return useMutation({
    mutationFn: ({ email, password }: { email: string; password: string }) => authApi.login(email, password),
    onSuccess: (result) => {
      if (!result.requiresTwoFactor && result.accessToken && result.refreshToken && result.userId) {
        setSession(result.accessToken, result.refreshToken, result.userId)
      }
    },
  })
}

export function useLoginTwoFactor() {
  const setSession = useAuthStore((s) => s.setSession)

  return useMutation({
    mutationFn: ({ userId, code, isRecoveryCode }: { userId: string; code: string; isRecoveryCode: boolean }) =>
      authApi.loginTwoFactor(userId, code, isRecoveryCode),
    onSuccess: (result) => {
      if (result.accessToken && result.refreshToken && result.userId) {
        setSession(result.accessToken, result.refreshToken, result.userId)
      }
    },
  })
}

export function useRegister() {
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
  })
}

export function useLogout() {
  const { refreshToken, clear } = useAuthStore()

  return useMutation({
    mutationFn: () => authApi.logout(refreshToken ?? ''),
    onSettled: () => clear(),
  })
}

export function useConfirmEmail() {
  return useMutation({
    mutationFn: ({ userId, token }: { userId: string; token: string }) => authApi.confirmEmail(userId, token),
  })
}

export function useChangePassword() {
  return useMutation({
    mutationFn: ({ currentPassword, newPassword }: { currentPassword: string; newPassword: string }) =>
      authApi.changePassword(currentPassword, newPassword),
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

  return useMutation({
    mutationFn: (code: string) => authApi.completeExternalLogin(code),
    onSuccess: (result) => {
      if (result.accessToken && result.refreshToken && result.userId) {
        setSession(result.accessToken, result.refreshToken, result.userId)
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
