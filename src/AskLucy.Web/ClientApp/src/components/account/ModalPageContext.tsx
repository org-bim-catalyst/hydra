import { createContext, useContext } from 'react'

/**
 * True while a page is being rendered inside the account modal rather than as a full route.
 *
 * `AppShell` reads it to drop the chrome that the modal already provides or that would be
 * wrong inside one: the sticky top bar (the account menu is how you got here — offering it
 * again inside itself invites opening a modal from a modal) and the full-viewport height.
 */
const ModalPageContext = createContext(false)

export const ModalPageProvider = ModalPageContext.Provider

export function useIsInModalPage() {
  return useContext(ModalPageContext)
}
