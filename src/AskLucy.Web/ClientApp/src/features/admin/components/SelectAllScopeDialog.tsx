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
import { useT } from '../../../i18n/useT'

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
export function SelectAllScopeDialog({
  open,
  onClose,
  verb,
  pageCount,
  totalCount,
  onChoose,
}: SelectAllScopeDialogProps) {
  const [scope, setScope] = useState<SelectionScopeChoice>('page')
  const t = useT('admin.users')
  const isSelect = verb === 'Select'

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
        <RadioGroup
          value={scope}
          onChange={(e) => setScope(e.target.value as SelectionScopeChoice)}
        >
          <FormControlLabel
            value="page"
            control={<Radio />}
            label={t(isSelect ? 'scope.selectPage' : 'scope.deselectPage', { count: pageCount })}
          />
          <FormControlLabel
            value="all"
            control={<Radio />}
            disabled={totalCount === undefined}
            label={
              totalCount === undefined
                ? t('scope.resolving')
                : t(isSelect ? 'scope.selectAll' : 'scope.deselectAll', { count: totalCount })
            }
          />
        </RadioGroup>
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose}>{t('scope.cancel')}</Button>
        <Button onClick={handleConfirm} variant="contained" autoFocus>
          {t(isSelect ? 'scope.select' : 'scope.deselect')}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
