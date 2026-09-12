import type { ReactNode } from 'react'
import { ButtonBase, Typography } from '@mui/material'
import { validateAction } from './allowlist'
import { useActionInvoker } from './useActionInvoker'
import type { ActionRequest } from '../content/blocks'

interface ActionAffordanceProps {
  /** Absent, unknown-command, or malformed-args all resolve the same way here: no action at all
   * (spec FR-013) — validated at render, via the same `validateAction` the framework uses
   * everywhere else, so an entry is never presented as activatable only to fail on click. */
  action?: ActionRequest
  children: ReactNode
}

/** contracts/action-allowlist.md — the one shared presentation every block that can carry an
 * action renders through (keyValue items, table rows via a dedicated cell, metric). An entry
 * with a valid action is visibly distinguishable, keyboard reachable and keyboard operable (spec
 * FR-013); an entry with none, or a rejected one, is plain content — not focusable, not styled as
 * activatable, no handler attached (spec FR-014). A failed invocation surfaces inline next to the
 * entry that failed, rather than a global notification (spec FR-015).
 *
 * Deliberately an inline `<span>`-rooted control rather than ever wrapping a whole table `<tr>`:
 * giving a row `role="button"` would override its `role="row"`, breaking the row/cell navigation
 * assistive technology relies on. `TableBlock` places this inside a dedicated cell instead. */
export function ActionAffordance({ action, children }: ActionAffordanceProps) {
  const { invoke, error } = useActionInvoker()

  if (!action || !validateAction(action.command, action.args).valid) {
    return <>{children}</>
  }

  return (
    <>
      <ButtonBase component="span" onClick={() => invoke(action)} sx={{ display: 'inline-flex', textAlign: 'inherit' }}>
        {children}
      </ButtonBase>
      {error && (
        <Typography variant="caption" color="error.main" role="alert" sx={{ display: 'block', mt: 0.5 }}>
          {error}
        </Typography>
      )}
    </>
  )
}
