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

export function confirmEmail(userId: string, token: string) {
  return apiFetch<void>('/auth/confirm-email', { method: 'POST', body: JSON.stringify({ userId, token }) })
}

export function changePassword(currentPassword: string, newPassword: string) {
  return apiFetch<void>('/auth/change-password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword }),
  })
}

export function requestEmailChange(newEmail: string) {
  return apiFetch<void>('/auth/change-email/request', { method: 'POST', body: JSON.stringify({ newEmail }) })
}

export function confirmEmailChange(userId: string, newEmail: string, token: string) {
  return apiFetch<void>('/auth/change-email/confirm', {
    method: 'POST',
    body: JSON.stringify({ userId, newEmail, token }),
  })
}

export interface ExternalLogin {
  provider: string
  providerKey: string
  displayName: string
}

export function getExternalLogins() {
  return apiFetch<ExternalLogin[]>('/auth/external-logins')
}

/** Exchanges the one-time code from the OAuth callback redirect for real tokens (FR-010). */
export function completeExternalLogin(code: string) {
  return apiFetch<AuthResponse>('/auth/external/complete', {
    method: 'POST',
    body: JSON.stringify({ code }),
  })
}

/**
 * Issued over a normal authenticated request before navigating the browser (top-level, no
 * Authorization header) to the link endpoint (FR-034) — see ExternalAuth.cs's doc comment.
 */
export function issueExternalLoginLinkTicket() {
  return apiFetch<string>('/auth/external/link-ticket', { method: 'POST' })
}

export function removeExternalLogin(provider: string, providerKey: string) {
  return apiFetch<void>(`/auth/external-logins/${encodeURIComponent(provider)}/${encodeURIComponent(providerKey)}`, {
    method: 'DELETE',
  })
}

export function enableTwoFactor() {
  return apiFetch<string>('/auth/2fa/enable', { method: 'POST' })
}

export function disableTwoFactor() {
  return apiFetch<void>('/auth/2fa/disable', { method: 'POST' })
}

export function generateRecoveryCodes() {
  return apiFetch<string[]>('/auth/2fa/recovery-codes', { method: 'POST' })
}
