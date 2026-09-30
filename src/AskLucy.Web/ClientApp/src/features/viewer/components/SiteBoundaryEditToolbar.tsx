import { alpha, Box, Button, IconButton, Paper, Typography, type Theme } from '@mui/material'
import { RiArrowGoBackLine, RiArrowGoForwardLine, RiCheckLine, RiCloseLine, RiLoader4Line } from '@remixicon/react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

/** The height of the HUD cards along the top edge (`HudCard`), so this bar shares their centreline. */
const BAR_HEIGHT_PX = 40

/** The workspace overlay's own margin from the top edge: 16 px on a phone, 24 px above it. */
const TOP_OFFSET = { xs: '16px', sm: '24px' }

/** A tinted surface distinct from the neutral HUD cards, so the editor reads as a temporary mode; follows the light/dark theme. */
const surface = {
  bgcolor: (t: Theme) => (t.palette.mode === 'dark' ? alpha('#2A2245', 0.96) : alpha('#EFE9FF', 0.97)),
  border: (t: Theme) => `1px solid ${alpha('#7C4DFF', t.palette.mode === 'dark' ? 0.55 : 0.4)}`,
  color: 'text.primary',
  backdropFilter: 'blur(12px)',
  boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
}

const formatArea = (squareMeters: number) => `about ${Math.round(squareMeters).toLocaleString('en-US')} m²`

const iconButtonSx = { width: BAR_HEIGHT_PX - 8, height: BAR_HEIGHT_PX - 8 }

/**
 * specs/079 contracts/edit-mode-viewer.md: the slim floating bar shown while the outline is being
 * edited - "Editing: <site>", the area, Undo/Redo, Cancel and Done, side by side on one 40 px row
 * that sits on the HUD cards' centreline, horizontally centred. It can be dismissed (the session
 * carries on; the Outline menu has every action and brings it back). A refusal, a save in progress,
 * a failed save or a conflict is explained in a small strip under the bar - and that strip shows
 * even while the bar is hidden, because those messages must never be missed.
 */
export function SiteBoundaryEditToolbar() {
  const session = useSiteBoundaryEditStore((s) => s.session)
  const dirty = useSiteBoundaryEditStore((s) => s.isDirty())
  const setToolbarHidden = useSiteBoundaryEditStore((s) => s.setToolbarHidden)
  if (!session) return null

  const { status } = session
  const saving = status.kind === 'saving'
  const message = session.refusal !== null || saving || status.kind === 'error' || status.kind === 'conflict'

  return (
    <Box
      sx={{
        position: 'absolute',
        top: TOP_OFFSET,
        left: '50%',
        transform: 'translateX(-50%)',
        zIndex: 5,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        gap: 0.75,
        maxWidth: 'calc(100% - 32px)',
        pointerEvents: 'none',
      }}
    >
      {!session.toolbarHidden && (
        <Paper
          role="toolbar"
          aria-label="Outline editor"
          elevation={0}
          sx={[
            {
              display: 'flex',
              alignItems: 'center',
              gap: 1,
              boxSizing: 'border-box',
              height: BAR_HEIGHT_PX,
              maxWidth: '100%',
              pl: 1.5,
              pr: 0.5,
              borderRadius: 2,
              pointerEvents: 'auto',
            },
            surface,
          ]}
        >
          <Typography variant="body2" noWrap sx={{ fontWeight: 600, minWidth: 0, maxWidth: 220 }}>
            Editing: {session.siteName}
          </Typography>
          <Typography variant="caption" color="text.secondary" noWrap sx={{ display: { xs: 'none', sm: 'block' } }}>
            {formatArea(session.approxAreaSquareMeters)}
          </Typography>

          <Box sx={{ width: '1px', alignSelf: 'stretch', my: 1, bgcolor: 'divider' }} />

          <IconButton
            size="small"
            aria-label="Undo"
            onClick={siteBoundaryEditActions.undo}
            disabled={session.undo.length === 0 || saving}
            sx={iconButtonSx}
          >
            <RiArrowGoBackLine size={18} />
          </IconButton>
          <IconButton
            size="small"
            aria-label="Redo"
            onClick={siteBoundaryEditActions.redo}
            disabled={session.redo.length === 0 || saving}
            sx={iconButtonSx}
          >
            <RiArrowGoForwardLine size={18} />
          </IconButton>

          <Box sx={{ width: '1px', alignSelf: 'stretch', my: 1, bgcolor: 'divider' }} />

          <Button size="small" color="inherit" onClick={siteBoundaryEditActions.cancel} disabled={saving} sx={{ minHeight: BAR_HEIGHT_PX - 8, px: 1.25 }}>
            Cancel
          </Button>
          <Button
            size="small"
            variant="contained"
            onClick={() => void siteBoundaryEditActions.done()}
            disabled={!dirty || saving}
            startIcon={saving ? <RiLoader4Line size={16} className="spin" /> : <RiCheckLine size={16} />}
            sx={{
              minHeight: BAR_HEIGHT_PX - 8,
              px: 1.5,
              '& .spin': { animation: 'edit-bar-spin 1s linear infinite' },
              '@keyframes edit-bar-spin': { to: { transform: 'rotate(360deg)' } },
            }}
          >
            {saving ? 'Saving' : 'Done'}
          </Button>

          <IconButton size="small" aria-label="Hide edit bar" title="Hide this bar - the Outline menu still has every action" onClick={() => setToolbarHidden(true)} sx={iconButtonSx}>
            <RiCloseLine size={18} />
          </IconButton>
        </Paper>
      )}

      {message && (
        <Paper
          elevation={0}
          sx={[
            { display: 'flex', alignItems: 'center', gap: 1, px: 1.5, py: 0.5, borderRadius: 2, pointerEvents: 'auto', maxWidth: '100%' },
            surface,
          ]}
        >
          {session.refusal !== null && (
            <Typography role="status" variant="body2" color="warning.main">
              {session.refusal}
            </Typography>
          )}
          {saving && (
            <Typography role="status" variant="body2" color="text.secondary">
              Saving&hellip;
            </Typography>
          )}
          {status.kind === 'error' && (
            <>
              <Typography role="alert" variant="body2" color="error.main">
                {status.message}
              </Typography>
              <Button color="inherit" size="small" onClick={() => void siteBoundaryEditActions.done()}>
                Retry
              </Button>
              <Button color="inherit" size="small" onClick={siteBoundaryEditActions.cancel}>
                Cancel
              </Button>
            </>
          )}
          {status.kind === 'conflict' && (
            <>
              <Typography role="alert" variant="body2" color="warning.main">
                The outline changed in another tab.
              </Typography>
              <Button color="inherit" size="small" onClick={() => void siteBoundaryEditActions.loadLatest()}>
                Load latest
              </Button>
              <Button color="inherit" size="small" onClick={siteBoundaryEditActions.cancel}>
                Cancel
              </Button>
            </>
          )}
        </Paper>
      )}
    </Box>
  )
}
