import { RiPauseLine, RiRefreshLine } from '@remixicon/react'
import { Fab } from '@mui/material'
import { useViewerEngineStore } from '../../../viewer/store/viewerEngineStore'
import { viewerEngine } from '../../../viewer/engine/viewerEngineInstance'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'

/** FR-014: an instant on/off toggle for the viewer's automatic rotation, independent of the
 * view-mode control. Styled like `ThemeToggleButton.tsx` (research.md Decision 5) — a direct
 * action with no expand/collapse state, not a `CircularAction` disclosure widget. */
export function RotationToggleButton() {
  const rotationEnabled = useViewerEngineStore((s) => s.camera.rotationEnabled)

  const toggle = () => {
    viewerEngine.setRotationEnabled(!rotationEnabled)
  }

  return (
    <Fab
      size="small"
      aria-label={rotationEnabled ? 'Stop rotation' : 'Start rotation'}
      aria-pressed={rotationEnabled}
      onClick={toggle}
      sx={{
        // 40 px, matching CircularAction's trigger Fab (FAB_PX). MUI's `medium` Fab is 48 px,
        // which left the account button visibly smaller than the two buttons beside it.
        width: 40,
        height: 40,
        minHeight: 40,
        boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
        bgcolor: CIRCULAR_ACTION_CHROME.collapsedBg,
        color: CIRCULAR_ACTION_CHROME.icon,
        border: CIRCULAR_ACTION_CHROME.border,
        backdropFilter: 'blur(12px)',
        '&:hover': { bgcolor: CIRCULAR_ACTION_CHROME.collapsedHoverBg, transform: 'scale(1.05)' },
        transition: (t) => t.transitions.create(['transform', 'background-color']),
      }}
    >
      {rotationEnabled ? <RiPauseLine /> : <RiRefreshLine />}
    </Fab>
  )
}
