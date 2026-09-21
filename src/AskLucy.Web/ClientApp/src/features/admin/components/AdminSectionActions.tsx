import { useContext } from 'react'
import { createPortal } from 'react-dom'
import type { ReactNode } from 'react'
import { AdminSectionActionsContext } from './adminSectionActionsContext'

/**
 * Renders a section's controls into the admin page header, beside the page title.
 *
 * Every admin section used to repeat its own title over a button in a row above its table — the
 * page header said "MCP servers" and the row below said "MCP Servers" again, costing a strip of
 * vertical space to say nothing. The button belongs in the header that already names the section;
 * a portal puts it there without hoisting the dialog state that drives it (that state is bound up
 * with the mutations, several components deep), which keeps this a layout change and nothing more.
 *
 * Outside an `AdminShell` — a panel rendered on its own in a test — the children render in place
 * rather than disappearing, so the control is still there to be found.
 */
export function AdminSectionActions({ children }: { children: ReactNode }) {
  const slot = useContext(AdminSectionActionsContext)
  if (slot === null) return <>{children}</>
  return createPortal(children, slot)
}
