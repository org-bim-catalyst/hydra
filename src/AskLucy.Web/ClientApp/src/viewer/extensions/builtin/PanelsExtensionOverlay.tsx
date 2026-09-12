import { Chip } from '@mui/material'
import { FloatingPanelHost } from '../../panels/components/FloatingPanelHost'
import { useFloatingPanelHub } from '../../panels/hooks/useFloatingPanelHub'

/** research D8 — bundles `FloatingPanelHost` with the "Reconnecting…" indicator `ViewerSurface`
 * used to render from `useFloatingPanelHub().isLive` directly. The hook holds the SignalR
 * connection, so it must stay mounted for exactly as long as this extension is started — which is
 * exactly the lifetime of a contributed overlay component (contributed at `start()`, unmounted
 * when `stop()` withdraws the contribution). */
export function PanelsExtensionOverlay() {
  const { isLive } = useFloatingPanelHub()

  return (
    <>
      <FloatingPanelHost />
      {/* specs/029-fix-chat-widget-bugs FR-010/analysis finding C1 — same Chip treatment
          ExecutionMonitor already uses for useWorkflowExecutionHub's isLive, adapted to only
          mount while reconnecting. */}
      {!isLive && (
        <Chip
          label="Reconnecting…"
          size="small"
          variant="outlined"
          color="default"
          data-testid="panel-hub-connection-status"
          sx={{
            position: 'absolute',
            bottom: { xs: 16, sm: 24 },
            left: {
              xs: 'calc(16px + min(25vh, 280px) + 12px)',
              sm: 'calc(24px + min(25vh, 280px) + 12px)',
            },
            bgcolor: 'background.paper',
          }}
        />
      )}
    </>
  )
}
