import { Alert, Snackbar } from '@mui/material'
import { useSiteBoundaryEditMode } from '../../../viewer/siteBoundaryEdit/useSiteBoundaryEditMode'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'
import { SiteBoundaryArcDraw } from './SiteBoundaryArcDraw'
import { SiteBoundaryBoxSelect } from './SiteBoundaryBoxSelect'
import { SiteBoundaryCircleDraw } from './SiteBoundaryCircleDraw'
import { SiteBoundaryEditToolbar } from './SiteBoundaryEditToolbar'
import { SiteBoundaryResetDialog } from './SiteBoundaryResetDialog'
import { SiteBoundaryShapeDialog } from './SiteBoundaryShapeDialog'

/**
 * specs/079: mounts outline edit mode inside the viewer, where the live map is. Renders the toolbar
 * while a session is open and the notice the editor owes the user when it cannot do what was asked
 * (entry failed, the map isn't ready, a forced exit). Contributed to the viewer by
 * `siteBoundaryExtension`, next to the outline overlay it replaces while editing.
 */
export function SiteBoundaryEditHost() {
  useSiteBoundaryEditMode()
  const notice = useSiteBoundaryEditStore((s) => s.notice)
  const setNotice = useSiteBoundaryEditStore((s) => s.setNotice)

  return (
    <>
      <SiteBoundaryBoxSelect />
      <SiteBoundaryArcDraw />
      <SiteBoundaryCircleDraw />
      <SiteBoundaryEditToolbar />
      <SiteBoundaryShapeDialog />
      <SiteBoundaryResetDialog />
      <Snackbar open={notice !== null} autoHideDuration={8000} onClose={() => setNotice(null)}>
        <Alert severity="warning" onClose={() => setNotice(null)} sx={{ width: '100%' }}>
          {notice}
        </Alert>
      </Snackbar>
    </>
  )
}
