import { useMutation } from '@tanstack/react-query'
import * as authApi from '../api/authApi'
import { useAuthStore } from '../../../store/authStore'

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
