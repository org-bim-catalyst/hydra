import {
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControl,
  FormControlLabel,
  FormLabel,
  Radio,
  RadioGroup,
  Stack,
  TextField,
} from '@mui/material'
import { useState, type FormEvent } from 'react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { equivalentRadius } from '../../../viewer/siteBoundaryEdit/ringShapes'
import { useSiteBoundaryEditStore, type CircleOperation, type ShapeTool } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

/** The tools that ask for one number and act on the ring being edited; a rectangle or square asks for its sides and an operation. */
type NumberTool = Exclude<ShapeTool, 'rectangle' | 'square'>

interface Copy {
  title: string
  label: string
  help: string
}

const COPY: Record<NumberTool, Copy> = {
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
const DEFAULT_VALUE: Record<Exclude<NumberTool, 'circle'>, number> = { round: 10, curve: 5 }

/**
 * specs/079: asks for the one number a shape tool needs, then applies it. The dialog stays open with
 * the reason when the tool refuses (a radius too big for the corner, a shape that would cross the
 * outline over itself), so the user can adjust the number and try again.
 */
export function SiteBoundaryShapeDialog() {
  const open = useSiteBoundaryEditStore((s) => s.shapeDialog) !== null
  const last = useSiteBoundaryEditStore((s) => s.shapeDialogHistory)
  // Stays mounted after it closes: MUI runs its exit and gives the page back. Unmounting at once left the editor inert.
  // Keyed by the opening, so each opening starts from that tool's own starting value.
  if (!last) return null
  if (last.tool === 'rectangle' || last.tool === 'square') return <TypedShapeForm key={last.seq} open={open} />
  return <ShapeForm key={last.seq} tool={last.tool} open={open} />
}

/**
 * specs/081 (FR-020): a rectangle or square of typed size, for the keyboard and touch, which have no drag.
 * It is placed on the centre of the ring being edited, then added to the outline or cut out of it.
 */
function TypedShapeForm({ open }: { open: boolean }) {
  const setShapeDialog = useSiteBoundaryEditStore((s) => s.setShapeDialog)
  const refusal = useSiteBoundaryEditStore((s) => s.session?.refusal ?? null)
  const [width, setWidth] = useState('20')
  const [height, setHeight] = useState('10')
  const [square, setSquare] = useState(false)
  const [east, setEast] = useState('0')
  const [north, setNorth] = useState('0')
  const [operation, setOperation] = useState<CircleOperation>('cut')
  const [busy, setBusy] = useState(false)

  const positive = (text: string) => text.trim() !== '' && Number.isFinite(Number(text)) && Number(text) >= 1
  const offset = (text: string) => text.trim() !== '' && Number.isFinite(Number(text))
  const valid = positive(width) && (square || positive(height)) && offset(east) && offset(north)

  // "Make square": the height is the width, and the height field follows and locks to it.
  const changeWidth = (text: string) => {
    setWidth(text)
    if (square) setHeight(text)
  }
  const changeSquare = (checked: boolean) => {
    setSquare(checked)
    if (checked) setHeight(width)
  }

  const apply = async (event: FormEvent) => {
    event.preventDefault()
    if (!valid || busy) return
    setBusy(true)
    try {
      const sideWidth = Number(width)
      const sideHeight = square ? sideWidth : Number(height)
      const shape = square ? 'square' : 'rectangle'
      if (await siteBoundaryEditActions.applyTypedShape(shape, operation, sideWidth, sideHeight, Number(east), Number(north))) setShapeDialog(null)
    } finally {
      setBusy(false)
    }
  }

  return (
    <Dialog open={open} onClose={() => setShapeDialog(null)} aria-labelledby="shape-dialog-title" maxWidth="xs" fullWidth>
      <form onSubmit={(event) => void apply(event)}>
        <DialogTitle id="shape-dialog-title">Add or cut a rectangle or square</DialogTitle>
        <DialogContent>
          <DialogContentText sx={{ mb: 2 }}>
            It is drawn with its sides running east-west and north-south, and its centre is the centre of the ring you are editing moved by the offsets
            below. Drag its corners afterwards to adjust it.
          </DialogContentText>
          <FormControl sx={{ mb: 2 }}>
            <FormLabel id="shape-operation-label">What to do with it</FormLabel>
            <RadioGroup row aria-labelledby="shape-operation-label" value={operation} onChange={(event) => setOperation(event.target.value as CircleOperation)}>
              <FormControlLabel value="cut" control={<Radio />} label="Cut it out" />
              <FormControlLabel value="add" control={<Radio />} label="Add it" />
            </RadioGroup>
          </FormControl>
          <FormControlLabel sx={{ mb: 1, display: 'block' }} control={<Checkbox checked={square} onChange={(event) => changeSquare(event.target.checked)} />} label="Make square" />
          <Stack direction="row" spacing={2}>
            <TextField
              autoFocus
              fullWidth
              type="number"
              label={square ? 'Side (metres)' : 'Width, east-west (metres)'}
              value={width}
              onChange={(event) => changeWidth(event.target.value)}
              error={width.trim() !== '' && !positive(width)}
              helperText={width.trim() !== '' && !positive(width) ? 'At least 1 m.' : ' '}
              slotProps={{ htmlInput: { step: 'any', inputMode: 'decimal' } }}
            />
            <TextField
              fullWidth
              type="number"
              label="Height, north-south (metres)"
              value={height}
              disabled={square}
              onChange={(event) => setHeight(event.target.value)}
              error={!square && height.trim() !== '' && !positive(height)}
              helperText={!square && height.trim() !== '' && !positive(height) ? 'At least 1 m.' : ' '}
              slotProps={{ htmlInput: { step: 'any', inputMode: 'decimal' } }}
            />
          </Stack>
          <Stack direction="row" spacing={2} sx={{ mt: 1 }}>
            <TextField
              fullWidth
              type="number"
              label="East of the ring centre (metres)"
              value={east}
              onChange={(event) => setEast(event.target.value)}
              error={east.trim() !== '' && !offset(east)}
              helperText=" "
              slotProps={{ htmlInput: { step: 'any', inputMode: 'decimal' } }}
            />
            <TextField
              fullWidth
              type="number"
              label="North of the ring centre (metres)"
              value={north}
              onChange={(event) => setNorth(event.target.value)}
              error={north.trim() !== '' && !offset(north)}
              helperText=" "
              slotProps={{ htmlInput: { step: 'any', inputMode: 'decimal' } }}
            />
          </Stack>
          {refusal && (
            <DialogContentText role="alert" color="error" sx={{ mt: 1 }}>
              {refusal}
            </DialogContentText>
          )}
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setShapeDialog(null)}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={!valid || busy}>
            Apply
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  )
}

function ShapeForm({ tool, open }: { tool: NumberTool; open: boolean }) {
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
    <Dialog open={open} onClose={() => setShapeDialog(null)} aria-labelledby="shape-dialog-title" maxWidth="xs" fullWidth>
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
