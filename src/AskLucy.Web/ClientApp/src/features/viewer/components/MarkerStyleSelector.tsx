import { useState } from 'react'
import { Fab, Menu, MenuItem, ListItemIcon, ListItemText, Tooltip } from '@mui/material'
import { RiMapPin2Line } from '@remixicon/react'
import { useMarkerStyleStore, type MarkerStyle } from '../../../store/markerStyleStore'
import { CIRCULAR_ACTION_CHROME } from '../../../components/workspace-shell/CircularAction'

const STYLE_LABELS: Record<MarkerStyle, string> = {
  'pulsing-ring': 'Pulsing Ring',
  'classic-pin': 'Classic Pin',
  '3d-highlight': '3D Highlight',
  'simple-dot': 'Simple Dot',
}

/** specs/038-viewer-poi-zoom T033/T034: compact floating control for cycling POI marker styles.
 * Mirrors the RotationToggleButton pattern — Fab + Menu — so the host can place it in the
 * top-cluster leading controls alongside ThemeToggleButton and RotationToggleButton. */
export function MarkerStyleSelector() {
  const markerStyle = useMarkerStyleStore((s) => s.markerStyle)
  const setMarkerStyle = useMarkerStyleStore((s) => s.setMarkerStyle)

  const [anchor, setAnchor] = useState<null | HTMLElement>(null)
  const open = Boolean(anchor)

  const handleOpen = (event: React.MouseEvent<HTMLElement>) => {
    setAnchor(event.currentTarget)
  }
  const handleClose = () => setAnchor(null)
  const handleSelect = (style: MarkerStyle) => {
    setMarkerStyle(style)
    setAnchor(null)
  }

  return (
    <>
      <Tooltip title={`Marker style: ${STYLE_LABELS[markerStyle]}`} placement="right">
        <Fab
          size="medium"
          aria-label="Change POI marker style"
          aria-haspopup="true"
          aria-expanded={open}
          onClick={handleOpen}
          sx={{
            boxShadow: '0 2px 10px rgba(0,0,0,0.28)',
            bgcolor: CIRCULAR_ACTION_CHROME.collapsedBg,
            color: CIRCULAR_ACTION_CHROME.icon,
            border: CIRCULAR_ACTION_CHROME.border,
            backdropFilter: 'blur(12px)',
            '&:hover': { bgcolor: CIRCULAR_ACTION_CHROME.collapsedHoverBg, transform: 'scale(1.05)' },
            transition: (t) => t.transitions.create(['transform', 'background-color']),
          }}
        >
          <RiMapPin2Line />
        </Fab>
      </Tooltip>
      <Menu
        anchorEl={anchor}
        open={open}
        onClose={handleClose}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'right' }}
        transformOrigin={{ vertical: 'top', horizontal: 'right' }}
        aria-label="POI marker style options"
      >
        {(Object.keys(STYLE_LABELS) as MarkerStyle[]).map((style) => (
          <MenuItem
            key={style}
            selected={markerStyle === style}
            onClick={() => handleSelect(style)}
            aria-current={markerStyle === style ? 'true' : undefined}
          >
            <ListItemIcon sx={{ minWidth: 32 }}>
              <RiMapPin2Line
                size={18}
                style={{ opacity: markerStyle === style ? 1 : 0.4 }}
                aria-hidden
              />
            </ListItemIcon>
            <ListItemText primary={STYLE_LABELS[style]} />
          </MenuItem>
        ))}
      </Menu>
    </>
  )
}
