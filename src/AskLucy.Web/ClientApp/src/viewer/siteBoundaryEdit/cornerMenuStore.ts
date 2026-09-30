import { create } from 'zustand'

/** specs/079 (T061): where the right-click / long-press menu on a corner is open, in screen pixels; null when closed. */
interface CornerMenuState {
  anchor: { x: number; y: number } | null
  open(x: number, y: number): void
  close(): void
}

export const useCornerMenuStore = create<CornerMenuState>((set) => ({
  anchor: null,
  open: (x, y) => set({ anchor: { x, y } }),
  close: () => set({ anchor: null }),
}))
