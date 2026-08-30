import { create } from 'zustand'

interface AccountModalState {
  /** The account destination currently shown as a modal, or null when none is. */
  openPath: string | null
  open: (path: string) => void
  close: () => void
}

/**
 * Account destinations open as a modal over whatever the user was already looking at, rather
 * than replacing it — the readdy.ai reference, where Settings, Knowledge Bases and the admin
 * dashboard all float above the page behind them.
 *
 * Deliberately its own state rather than a route change. The alternative, React Router's
 * background-location pattern, makes every one of these destinations a navigation whose
 * backdrop depends on where you came from — and from a fresh tab there is nothing behind it at
 * all. The routes still exist and still work as full pages, so a deep link or a bookmark is
 * unaffected; this only changes what happens when you pick an item from the account menu.
 */
export const useAccountModalStore = create<AccountModalState>((set) => ({
  openPath: null,
  open: (path) => set({ openPath: path }),
  close: () => set({ openPath: null }),
}))
