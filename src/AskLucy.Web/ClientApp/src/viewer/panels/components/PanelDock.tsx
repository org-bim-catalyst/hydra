import { RiHistoryLine, RiLayoutGridLine } from '@remixicon/react'
import { Box, Divider, IconButton, Paper, Stack, Tooltip, Typography } from '@mui/material'
import { RESERVED_ATTRIBUTE } from '../layout/reservedRegions'
import { useFloatingPanelStore } from '../store/floatingPanelStore'

export interface PanelDockProps {
  /** specs/054 FR-005e — clears every panel's `manuallyPlaced` flag (`arrangeAll`) and runs a
   * fresh arrangement pass. Passed in from `FloatingPanelHost` rather than called directly,
   * because only the host has the DOM access a fresh pass needs (`collectReservedRects`). */
  onArrange: () => void
}

/** specs/054 research D7 — a left-edge, vertically-centered rail: the one screen edge no other
 * page/viewer chrome already claims (`WorkspaceOverlay` owns top-right and bottom-right,
 * `ExtensionToolbar`/`CameraAttitudeWidget` top-right, a weather widget and boundary-confidence
 * badge top-left, the panel-hub indicator bottom-left). Declares itself reserved (contracts/
 * reserved-regions.md) so the placement system it hosts can never collide with it.
 *
 * Hosts two things: the explicit "arrange" action (User Story 1, FR-005e) and the reopen tray
 * (User Story 2, FR-006/FR-007/FR-009) — a list of recently-closed panels the user can click back
 * open. `reopenPanel` is called directly from the store (no host-passed callback needed, unlike
 * `onArrange`): reopening only calls the existing `openPanel` internally, which
 * `FloatingPanelHost`'s own open/close-count effect already reacts to. Renders nothing while
 * there is nothing to show — no open panels AND no closed ones (FR-008). */
export function PanelDock({ onArrange }: PanelDockProps) {
  const hasOpenPanels = useFloatingPanelStore((s) => s.panels.length > 0)
  const closedPanels = useFloatingPanelStore((s) => s.closedPanels)
  const reopenPanel = useFloatingPanelStore((s) => s.reopenPanel)

  if (!hasOpenPanels && closedPanels.length === 0) return null

  return (
    <Box
      {...{ [RESERVED_ATTRIBUTE]: '' }}
      sx={{
        position: 'absolute',
        left: { xs: 16, sm: 24 },
        top: '50%',
        transform: 'translateY(-50%)',
        zIndex: 2,
        pointerEvents: 'auto',
      }}
    >
      <Paper elevation={2} sx={{ p: 0.5, bgcolor: 'background.paper', maxWidth: 220 }}>
        <Stack spacing={0.5}>
          {hasOpenPanels && (
            <Tooltip title="Arrange panels">
              <IconButton size="small" aria-label="Arrange panels" onClick={onArrange} sx={{ alignSelf: 'flex-start' }}>
                <RiLayoutGridLine size={18} />
              </IconButton>
            </Tooltip>
          )}
          {hasOpenPanels && closedPanels.length > 0 && <Divider flexItem />}
          {closedPanels.length > 0 && (
            <Stack component="ul" spacing={0.25} sx={{ listStyle: 'none', m: 0, p: 0 }} aria-label="Recently closed panels">
              {closedPanels.map((entry) => (
                <Box
                  key={entry.request.requestId}
                  component="li"
                  sx={{ display: 'flex', alignItems: 'center', gap: 0.5, px: 0.5 }}
                >
                  <RiHistoryLine size={14} aria-hidden style={{ flexShrink: 0, opacity: 0.6 }} />
                  <Tooltip title={`Reopen ${entry.request.title}`}>
                    <Typography
                      component="button"
                      onClick={() => reopenPanel(entry.request.requestId)}
                      variant="caption"
                      noWrap
                      sx={{
                        border: 0,
                        bgcolor: 'transparent',
                        p: 0,
                        m: 0,
                        cursor: 'pointer',
                        textAlign: 'left',
                        color: 'text.primary',
                        flex: 1,
                        minWidth: 0,
                        '&:hover': { textDecoration: 'underline' },
                      }}
                    >
                      {entry.request.title}
                    </Typography>
                  </Tooltip>
                </Box>
              ))}
            </Stack>
          )}
        </Stack>
      </Paper>
    </Box>
  )
}
