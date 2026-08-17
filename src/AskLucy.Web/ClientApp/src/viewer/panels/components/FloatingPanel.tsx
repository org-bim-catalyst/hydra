import { RiCloseLine } from '@remixicon/react'
import { Box, IconButton, Typography, alpha } from '@mui/material'
import { panelTypeRegistry } from '../registry'
import { useFloatingPanelStore } from '../store/floatingPanelStore'
import type { FloatingPanel as FloatingPanelModel } from '../types/panel'

/** FR-010 — panels render semi-transparent by default, independent of the opacity *preference*
 * feature (Settings "Viewer" tab, spec 028 User Story 3), which doesn't exist until later in the
 * build. Story 3 (T066) swaps this hardcoded value for the live `panelPreferencesStore` value. */
const DEFAULT_PANEL_OPACITY_PERCENT = 85

export interface FloatingPanelProps {
  panel: FloatingPanelModel
}

/** The chrome for a single open AI-requested panel (data-model.md "Floating Panel"). This is the
 * Foundational shell only — title bar and a close button, absolutely positioned/sized from
 * `floatingPanelStore`, rendering the resolved type's renderer or a visible fallback for an
 * unknown/invalid request (FR-016/FR-017). Drag, resize, minimize, and focus-on-interaction are
 * added in User Story 2 (`react-rnd` wiring); this component is intentionally namespaced under
 * `viewer/panels/` rather than reusing `components/workspace-shell/FloatingPanel.tsx`, which is an
 * unrelated single-instance workspace-control drawer (research.md Decision 5). */
export function FloatingPanel({ panel }: FloatingPanelProps) {
  const closePanel = useFloatingPanelStore((s) => s.closePanel)
  const definition = panelTypeRegistry.resolve(panel.typeKey)
  const Renderer = definition?.renderer

  return (
    <Box
      role="region"
      aria-label={panel.title}
      sx={{
        position: 'absolute',
        left: panel.position.x,
        top: panel.position.y,
        width: panel.size.width,
        height: panel.size.height,
        display: 'flex',
        flexDirection: 'column',
        overflow: 'hidden',
        borderRadius: 2,
        boxShadow: 4,
        zIndex: panel.zOrder,
        bgcolor: (theme) => alpha(theme.palette.background.paper, DEFAULT_PANEL_OPACITY_PERCENT / 100),
        color: 'text.primary',
        pointerEvents: 'auto',
      }}
    >
      <Box
        sx={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          px: 1.5,
          py: 1,
          borderBottom: 1,
          borderColor: 'divider',
          flexShrink: 0,
        }}
      >
        <Typography variant="subtitle2" noWrap sx={{ flex: 1, minWidth: 0 }}>
          {panel.title}
        </Typography>
        <IconButton onClick={() => closePanel(panel.id)} aria-label="Close panel" size="small">
          <RiCloseLine size={18} />
        </IconButton>
      </Box>
      <Box sx={{ flex: 1, minHeight: 0, overflow: 'auto', p: 1.5 }}>
        {panel.validationStatus === 'unknown-type' && (
          <Typography variant="body2" color="text.secondary">
            Unsupported panel type &quot;{panel.typeKey}&quot;.
          </Typography>
        )}
        {panel.validationStatus === 'invalid' && (
          <Box>
            <Typography variant="body2" color="text.secondary">
              This panel&apos;s data couldn&apos;t be loaded.
            </Typography>
            {panel.validationError && (
              <Typography
                component="details"
                variant="caption"
                color="text.disabled"
                sx={{ mt: 1 }}
              >
                <Box component="summary" sx={{ cursor: 'pointer' }}>
                  Details
                </Box>
                {panel.validationError}
              </Typography>
            )}
          </Box>
        )}
        {panel.validationStatus === 'valid' && Renderer && <Renderer data={panel.data} />}
      </Box>
    </Box>
  )
}
