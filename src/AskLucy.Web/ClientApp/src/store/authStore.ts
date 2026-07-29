import { create } from 'zustand'
import { persist } from 'zustand/middleware'

interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  userId: string | null
  setSession: (accessToken: string, refreshToken: string, userId: string) => void
  clear: () => void
}

/**
 * Client/UI auth session state (Zustand) — server-fetched data (chats, profile) lives in
 * TanStack Query instead, per constitution §7 (State management).
 */
export const useAuthStore = create<AuthState>()(
  persist(
    (set) => ({
      accessToken: null,
      refreshToken: null,
      userId: null,
      setSession: (accessToken, refreshToken, userId) => set({ accessToken, refreshToken, userId }),
      clear: () => set({ accessToken: null, refreshToken: null, userId: null }),
    }),
    { name: 'ask-lucy-auth' },
  ),
)
