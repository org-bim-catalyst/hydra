import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  accessToken: string | null
  userId: string | null
  setSession: (accessToken: string, userId: string) => void
  clear: () => void
}

/**
 * Client/UI auth session state (Zustand) — server-fetched data (chats, profile) lives in
 * TanStack Query instead, per constitution §7 (State management). The refresh token lives
 * only in an httpOnly cookie set by the backend, never here — see useSession/httpClient.
 */
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      userId: null,
      setSession: (accessToken, userId) => set({ accessToken, userId }),
      clear: () => set({ accessToken: null, userId: null }),
    }),
    { name: 'ask-lucy-auth' },
  ),
)
