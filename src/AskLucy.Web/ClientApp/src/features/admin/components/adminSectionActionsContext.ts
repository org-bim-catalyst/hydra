import { createContext } from 'react'

/** The header node {@link AdminShell} offers up for a section's own controls, or null outside one. */
export const AdminSectionActionsContext = createContext<HTMLElement | null>(null)
