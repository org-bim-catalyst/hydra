import { RiUserSettingsLine } from '@remixicon/react'
import { Fab } from '@mui/material'
import { UserMenu } from '../UserMenu'
import { CIRCULAR_ACTION_CHROME } from './CircularAction'
import { useWorkspaceOverlayStore } from '../../store/workspaceOverlayStore'

/**
 * The Studio workspace's account button. Only the trigger is local — the menu itself is the
 * one `UserMenu` the rest of the app uses, so the workspace can keep its circular chrome
 * without owning a second copy of the destination list.
 */
export function StudioAccountMenuButton() {
  const collapse = useWorkspaceOverlayStore((s) => s.collapse)

  return (
    <UserMenu
      renderTrigger={({ onClick, open }) => (
        <Fab
          size="small"
          aria-label="Account menu"
          aria-expanded={open}
          // FR-015's mutual exclusivity still holds even though this menu is no longer a
          // workspaceOverlayStore control: opening it collapses whatever tool control was
          // expanded, so the workspace never shows two open panels at once.
          onClick={(event) => {
            collapse()
            onClick(event)
          }}
          sx={{
            // 40 px, matching the theme and rotation buttons beside it.
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
          <RiUserSettingsLine size={20} />
        </Fab>
      )}
    />
  )
}
