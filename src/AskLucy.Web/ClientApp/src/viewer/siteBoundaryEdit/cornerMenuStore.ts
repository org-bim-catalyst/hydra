import { create } from 'zustand'

/** specs/079 (T061): where the right-click / long-press menu on a corner is open, in screen pixels; null when closed. */
interface CornerMenuState {
  anchor: { x: number; y: number } | null
  /** specs/081: set when the menu was opened on a corner of a void: which one, so "Remove void" knows what to fill in. */
  voidTarget: { ring: number; voidIndex: number } | null
  open(x: number, y: number): void
  /** The menu on a corner of a void, which offers to remove that void. */
  openVoid(x: number, y: number, ring: number, voidIndex: number): void
  close(): void
}

export const useCornerMenuStore = create<CornerMenuState>((set) => ({
  anchor: null,
  voidTarget: null,
  open: (x, y) => set({ anchor: { x, y }, voidTarget: null }),
  openVoid: (x, y, ring, voidIndex) => set({ anchor: { x, y }, voidTarget: { ring, voidIndex } }),
  close: () => set({ anchor: null, voidTarget: null }),
}))
