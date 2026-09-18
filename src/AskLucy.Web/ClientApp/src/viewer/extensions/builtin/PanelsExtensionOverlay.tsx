import { Chip } from '@mui/material'
import { useSiteAnalysisHub } from '../../../features/siteAnalysis/hooks/useSiteAnalysisHub'
import { FloatingPanelHost } from '../../panels/components/FloatingPanelHost'
import { useFloatingPanelHub } from '../../panels/hooks/useFloatingPanelHub'

/** research D8 — bundles `FloatingPanelHost` with the "Reconnecting…" indicator `ViewerSurface`
 * used to render from `useFloatingPanelHub().isLive` directly. The hook holds the SignalR
 * connection, so it must stay mounted for exactly as long as this extension is started — which is
 * exactly the lifetime of a contributed overlay component (contributed at `start()`, unmounted
 * when `stop()` withdraws the contribution).
 *
 * specs/057-site-analysis-agent — `useSiteAnalysisHub` is mounted here too, not at the app shell:
 * a site analysis's panel and chat notice always arrive together for the same viewer-active
 * surface, so coupling both hubs' connection lifetimes to this same extension keeps them
 * consistent rather than one outliving the other. */
export function PanelsExtensionOverlay() {
  const { isLive } = useFloatingPanelHub()
  useSiteAnalysisHub()

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
