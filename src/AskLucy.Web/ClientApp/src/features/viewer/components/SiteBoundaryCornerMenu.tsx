import { ListItemIcon, ListItemText, Menu, MenuItem } from '@mui/material'
import { RiDeleteBinLine } from '@remixicon/react'
import { siteBoundaryEditActions } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditActions'
import { useCornerMenuStore } from '../../../viewer/siteBoundaryEdit/cornerMenuStore'
import { useSiteBoundaryEditStore } from '../../../viewer/siteBoundaryEdit/siteBoundaryEditStore'

const MIN_CORNERS = 3

/**
 * specs/079 (T061): the menu on a corner, opened by right-click or long-press. The controller has already
 * selected that corner, so "Delete corner" acts on it like the Outline menu's entry does.
 */
export function SiteBoundaryCornerMenu() {
  const anchor = useCornerMenuStore((s) => s.anchor)
  const close = useCornerMenuStore((s) => s.close)
  const cornerCount = useSiteBoundaryEditStore((s) => {
    const session = s.session
    return session ? (session.rings[session.activeRing]?.length ?? 0) : 0
  })
  const tooFew = cornerCount <= MIN_CORNERS

  return (
    <Menu
      open={anchor !== null}
      onClose={close}
      anchorReference="anchorPosition"
      anchorPosition={anchor ? { left: anchor.x, top: anchor.y } : undefined}
      slotProps={{ list: { dense: true } }}
    >
      <MenuItem
        disabled={tooFew}
        onClick={() => {
          close()
          siteBoundaryEditActions.deleteCorner()
        }}
      >
        <ListItemIcon>
          <RiDeleteBinLine size={18} />
        </ListItemIcon>
        <ListItemText secondary={tooFew ? 'An outline needs at least 3 corners' : undefined}>Delete corner</ListItemText>
      </MenuItem>
    </Menu>
  )
}
