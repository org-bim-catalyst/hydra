import { apiFetch } from '../../../api/httpClient'
import { useAuthStore } from '../../../store/authStore'

export interface UserProfile {
  id: string
  email: string
  firstName: string | null
  lastName: string | null
  birthDate: string
  twoFactorEnabled: boolean
  avatarFileName: string | null
}

export const getMyProfile = () => apiFetch<UserProfile>('/users/me')

export const updateMyProfile = (firstName?: string, lastName?: string) =>
  apiFetch<void>('/users/me', { method: 'PATCH', body: JSON.stringify({ firstName, lastName }) })

export async function uploadAvatar(file: File): Promise<string> {
  const accessToken = useAuthStore.getState().accessToken
  const form = new FormData()
  form.append('file', file)

  const API_BASE_URL = import.meta.env.VITE_API_BASE_URL ?? '/api/v1'
  const response = await fetch(`${API_BASE_URL}/users/me/avatar`, {
    method: 'PUT',
    headers: accessToken ? { Authorization: `Bearer ${accessToken}` } : undefined,
    body: form,
  })

  if (!response.ok) {
    throw new Error(`Avatar upload failed with ${response.status}`)
  }

  const result = (await response.json()) as { avatarUrl: string }
  return result.avatarUrl
}
