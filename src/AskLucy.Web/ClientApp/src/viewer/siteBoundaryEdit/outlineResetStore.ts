import { create } from 'zustand'

/** specs/079 (US5): whether the "Reset to Lucy's outline" confirmation is open. Lives outside the edit session: reset happens when no edit is open. */
interface OutlineResetState {
  open: boolean
  show(): void
  hide(): void
}

export const useOutlineResetStore = create<OutlineResetState>((set) => ({
  open: false,
  show: () => set({ open: true }),
  hide: () => set({ open: false }),
}))
