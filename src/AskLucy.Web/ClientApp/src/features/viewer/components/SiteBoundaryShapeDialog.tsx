import { Button, Dialog, DialogActions, DialogContent, DialogContentText, DialogTitle, TextField } from '@mui/material'
import { useState, type FormEvent } from 'react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { equivalentRadius } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { useSiteBoundaryEditStore, type ShapeTool } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

interface Copy {
  title: string
  label: string
  help: string
}

const COPY: Record<ShapeTool, Copy> = {
  round: {
    title: 'Round this corner',
    label: 'Radius (metres)',
    help: 'The corner is replaced by a smooth curve of this radius. A bigger radius rounds more of the edges.',
  },
  curve: {
    title: 'Curve this edge',
    label: 'Bulge (metres)',
    help: 'The edge after the selected corner bends into an arc that bulges this far at its middle. Use a negative number to bend it inward.',
  },
  circle: {
    title: 'Make this ring a circle',
    label: 'Radius (metres)',
    help: 'The ring is replaced by a circle around its centre. The starting radius gives the same area as the ring has now.',
  },
}

/** The starting value: a modest, visible change for a corner or an edge, and the same-area circle for a ring. */
const DEFAULT_VALUE: Record<Exclude<ShapeTool, 'circle'>, number> = { round: 10, curve: 5 }

/**
 * specs/079: asks for the one number a shape tool needs, then applies it. The dialog stays open with
 * the reason when the tool refuses (a radius too big for the corner, a shape that would cross the
 * outline over itself), so the user can adjust the number and try again.
 */
export function SiteBoundaryShapeDialog() {
  const tool = useSiteBoundaryEditStore((s) => s.shapeDialog)
  if (!tool) return null

  // Keyed by the tool, so each opening starts from that tool's own starting value.
  return <ShapeForm key={tool} tool={tool} />
}

function ShapeForm({ tool }: { tool: ShapeTool }) {
  const setShapeDialog = useSiteBoundaryEditStore((s) => s.setShapeDialog)
  const refusal = useSiteBoundaryEditStore((s) => s.session?.refusal ?? null)
  // Read once, when the dialog opens: the area moves as the user edits, but the number they typed must not.
  const [value, setValue] = useState(() =>
    String(
      tool === 'circle'
        ? Math.round(equivalentRadius(useSiteBoundaryEditStore.getState().session?.approxAreaSquareMeters ?? 0))
        : DEFAULT_VALUE[tool],
    ),
  )

  const copy = COPY[tool]
  const number = Number(value)
  const valid = value.trim() !== '' && Number.isFinite(number) && number !== 0 && (tool === 'curve' || number > 0)

  const apply = (event: FormEvent) => {
    event.preventDefault()
    if (!valid) return
    if (siteBoundaryEditActions.applyShape(tool, number)) setShapeDialog(null)
  }

  return (
    <Dialog open onClose={() => setShapeDialog(null)} aria-labelledby="shape-dialog-title" maxWidth="xs" fullWidth>
      <form onSubmit={apply}>
        <DialogTitle id="shape-dialog-title">{copy.title}</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>{copy.help}</DialogContentText>
          <TextField
            autoFocus
            fullWidth
            type="number"
            label={copy.label}
            value={value}
            onChange={(event) => setValue(event.target.value)}
            error={value.trim() !== '' && !valid}
            helperText={value.trim() !== '' && !valid ? 'Enter a number greater than zero.' : ' '}
            slotProps={{ htmlInput: { step: 'any', inputMode: 'decimal' } }}
          />
          {refusal && (
            <DialogContentText role="alert" color="error" sx={{ mt: 1 }}>
              {refusal}
            </DialogContentText>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setShapeDialog(null)}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={!valid}>
            Apply
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}
