import { RiHistoryLine } from '@remixicon/react'
import { Box, Paper, Stack, Tooltip, Typography } from '@mui/material'
import { RESERVED_ATTRIBUTE } from '../layout/reservedRegions'
import { useFloatingPanelStore } from '../store/floatingPanelStore'

/** specs/054 research D7 — a left-edge, vertically-centered rail: the one screen edge no other
 * page/viewer chrome already claims (`WorkspaceOverlay` owns top-right and bottom-right,
 * `ExtensionToolbar`/`CameraAttitudeWidget` top-right, a weather widget and boundary-confidence
 * badge top-left, the panel-hub indicator bottom-left). Declares itself reserved (contracts/
 * reserved-regions.md) so the placement system it hosts can never collide with it.
 *
 * Hosts the reopen tray (User Story 2, FR-006/FR-007/FR-009) — a list of recently-closed panels
 * the user can click back open. `reopenPanel` is called directly from the store: reopening only
 * calls the existing `openPanel` internally, which `FloatingPanelHost`'s own open/close-count
 * effect already reacts to. Renders nothing while nothing has been closed (FR-008).
 *
 * The "arrange" action (User Story 1, FR-005e) used to sit here too. It moved to the viewer
 * toolbar on the right (`panelsExtension`), beside the other one-tap viewer controls. */
export function PanelDock() {
  const closedPanels = useFloatingPanelStore((s) => s.closedPanels)
  const reopenPanel = useFloatingPanelStore((s) => s.reopenPanel)

  if (closedPanels.length === 0) return null

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
