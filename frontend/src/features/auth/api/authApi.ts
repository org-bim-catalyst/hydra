import { apiFetch } from '../../../api/httpClient'

export interface AuthResponse {
  userId: string | null
  accessToken: string | null
  expiresAtUtc: string | null
  refreshToken: string | null
  requiresTwoFactor: boolean
}

export function login(email: string, password: string) {
  return apiFetch<AuthResponse>('/auth/login', {
    method: 'POST',
    body: JSON.stringify({ email, password }),
  })
}

export function loginTwoFactor(userId: string, code: string, isRecoveryCode: boolean) {
  return apiFetch<AuthResponse>('/auth/login/2fa', {
    method: 'POST',
    body: JSON.stringify({ userId, code, isRecoveryCode }),
  })
}

export function register(email: string, password: string, firstName?: string, lastName?: string) {
  return apiFetch<AuthResponse>('/auth/register', {
    method: 'POST',
    body: JSON.stringify({ email, password, firstName, lastName }),
  })
}

export function refresh(refreshToken: string) {
  return apiFetch<AuthResponse>('/auth/refresh', {
    method: 'POST',
    body: JSON.stringify({ refreshToken }),
  })
}

export function logout(refreshToken: string) {
  return apiFetch<void>('/auth/logout', {
    method: 'POST',
    body: JSON.stringify({ refreshToken }),
  })
}
