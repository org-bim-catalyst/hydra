import { useState } from 'react'
import {
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  FormControlLabel,
  Radio,
  RadioGroup,
} from '@mui/material'

export type SelectionScopeChoice = 'page' | 'all'

interface SelectAllScopeDialogProps {
  open: boolean
  onClose: () => void
  /** "Select" or "Deselect" — which direction the header checkbox just requested. */
  verb: 'Select' | 'Deselect'
  pageCount: number
  /** Total rows matching the active filter across every page, once resolved. `undefined` while resolving. */
  totalCount?: number
  onChoose: (scope: SelectionScopeChoice) => void
}

/** Asks whether a header-checkbox click should apply to this page only or to every matching row, so a selection made once survives paging back and forth. */
export function SelectAllScopeDialog({ open, onClose, verb, pageCount, totalCount, onChoose }: SelectAllScopeDialogProps) {
  const [scope, setScope] = useState<SelectionScopeChoice>('page')

  function handleClose() {
    setScope('page')
    onClose()
  }

  function handleConfirm() {
    onChoose(scope)
    setScope('page')
  }

  return (
    <Dialog open={open} onClose={handleClose} fullWidth maxWidth="xs">
      <DialogContent>
        <RadioGroup value={scope} onChange={(e) => setScope(e.target.value as SelectionScopeChoice)}>
          <FormControlLabel value="page" control={<Radio />} label={`${verb} the ${pageCount} item${pageCount === 1 ? '' : 's'} on this page only`} />
          <FormControlLabel
            value="all"
            control={<Radio />}
            disabled={totalCount === undefined}
            label={totalCount === undefined ? 'Resolving total matching…' : `${verb} all ${totalCount} matching items`}
          />
        </RadioGroup>
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose}>Cancel</Button>
        <Button onClick={handleConfirm} variant="contained" autoFocus>
          {verb}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
