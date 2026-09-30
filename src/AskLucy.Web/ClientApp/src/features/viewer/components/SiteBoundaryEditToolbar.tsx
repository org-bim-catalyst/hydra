import { Alert, Box, Button, IconButton, Paper, Stack, Typography } from '@mui/material'
import { RiArrowGoBackLine, RiArrowGoForwardLine, RiCheckLine, RiCloseLine } from '@remixicon/react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

const TOUCH_TARGET_PX = 44

const formatArea = (squareMeters: number) => `about ${Math.round(squareMeters).toLocaleString('en-US')} m\u00b2`

/**
 * specs/079 contracts/edit-mode-viewer.md: the bar shown while the outline is being edited - what
 * is being edited, the area as it changes, Undo/Redo, Cancel and Done, and the one status line that
 * explains a refusal, a save in progress, a failed save, or a conflict. Every button is at least
 * 44 px so it can be pressed on a touch screen.
 */
export function SiteBoundaryEditToolbar() {
  const session = useSiteBoundaryEditStore((s) => s.session)
  const dirty = useSiteBoundaryEditStore((s) => s.isDirty())
  if (!session) return null

  const { status } = session
  const saving = status.kind === 'saving'

  return (
    <Paper
      role="toolbar"
      aria-label="Outline editor"
      elevation={6}
      sx={{
        position: 'absolute',
        top: 72,
        left: '50%',
        transform: 'translateX(-50%)',
        zIndex: 5,
        px: 2,
        py: 1,
        maxWidth: 'min(640px, calc(100% - 32px))',
        borderRadius: 3,
        backdropFilter: 'blur(12px)',
      }}
    >
      <Stack direction="row" spacing={1.5} useFlexGap sx={{ alignItems: "center", flexWrap: "wrap" }}>
        <Box sx={{ minWidth: 0, flex: '1 1 180px' }}>
          <Typography variant="subtitle2" noWrap>
            Editing: {session.siteName}
          </Typography>
          <Typography variant="caption" color="text.secondary">
            {formatArea(session.approxAreaSquareMeters)}
          </Typography>
        </Box>

        <IconButton
          aria-label="Undo"
          onClick={siteBoundaryEditActions.undo}
          disabled={session.undo.length === 0 || saving}
          sx={{ width: TOUCH_TARGET_PX, height: TOUCH_TARGET_PX }}
        >
          <RiArrowGoBackLine />
        </IconButton>
        <IconButton
          aria-label="Redo"
          onClick={siteBoundaryEditActions.redo}
          disabled={session.redo.length === 0 || saving}
          sx={{ width: TOUCH_TARGET_PX, height: TOUCH_TARGET_PX }}
        >
          <RiArrowGoForwardLine />
        </IconButton>
        <Button
          onClick={siteBoundaryEditActions.cancel}
          disabled={saving}
          startIcon={<RiCloseLine />}
          sx={{ minHeight: TOUCH_TARGET_PX }}
        >
          Cancel
        </Button>
        <Button
          variant="contained"
          onClick={() => void siteBoundaryEditActions.done()}
          disabled={!dirty || saving}
          startIcon={<RiCheckLine />}
          sx={{ minHeight: TOUCH_TARGET_PX }}
        >
          Done
        </Button>
      </Stack>

      {session.refusal && (
        <Typography role="status" variant="body2" color="warning.main" sx={{ mt: 1 }}>
          {session.refusal}
        </Typography>
      )}
      {saving && (
        <Typography role="status" variant="body2" color="text.secondary" sx={{ mt: 1 }}>
          Saving&hellip;
        </Typography>
      )}
      {status.kind === 'error' && (
        <Alert
          severity="error"
          sx={{ mt: 1 }}
          action={
            <Button color="inherit" size="small" onClick={() => void siteBoundaryEditActions.done()}>
              Retry
            </Button>
          }
        >
          {status.message}
        </Alert>
      )}
      {status.kind === 'conflict' && (
        <Alert
          severity="warning"
          sx={{ mt: 1 }}
          action={
            <>
              <Button color="inherit" size="small" onClick={() => void siteBoundaryEditActions.loadLatest()}>
                Load latest
              </Button>
              <Button color="inherit" size="small" onClick={siteBoundaryEditActions.cancel}>
                Cancel
              </Button>
            </>
          }
        >
          The outline changed in another tab.
        </Alert>
      )}
    </Paper>
  )
}
