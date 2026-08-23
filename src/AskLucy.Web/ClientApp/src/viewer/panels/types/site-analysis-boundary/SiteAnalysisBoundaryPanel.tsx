import { useEffect, useRef } from 'react'
import { Chip, Stack, Typography } from '@mui/material'
import { z } from 'zod'
import { panelTypeRegistry } from '../../registry'
import { viewerEngine } from '../../../engine/viewerEngineInstance'

/**
 * specs/050-park-site-analysis-agent — pushed by `SiteAnalysisCompletionReactionJob` via the
 * existing `IPanelNotifier`/`PanelHub` transport (contracts/panel-type-registry.md's mechanism)
 * once `resolve_site_boundary` completes. The Immersive Viewer's command API
 * (`viewer/api/commands.ts`) is client-side only — there is no separate backend→frontend viewer
 * push channel — so this panel type's renderer is what actually forwards the resolved boundary
 * into the viewer (`addLayer` + `zoomToLocation`) as a one-time side effect on mount, then
 * displays as a normal, dismissible confirmation panel like any other type (FR-003).
 */
export const siteAnalysisBoundaryDataSchema = z.object({
  resolvedName: z.string().nullable(),
  latitude: z.number().nullable(),
  longitude: z.number().nullable(),
  builtAssetConfirmed: z.boolean(),
})

export type SiteAnalysisBoundaryData = z.infer<typeof siteAnalysisBoundaryDataSchema>

function SiteAnalysisBoundaryPanelRenderer({ data }: { data: SiteAnalysisBoundaryData }) {
  const hasDispatched = useRef(false)

  useEffect(() => {
    if (hasDispatched.current || data.latitude === null || data.longitude === null) {
      return
    }
    hasDispatched.current = true

    viewerEngine.addLayer({
      kind: 'gis',
      metadata: { center: { latitude: data.latitude, longitude: data.longitude } },
    })
    viewerEngine.zoomToLocation(data.latitude, data.longitude)
    // Command results are intentionally not surfaced as user-facing errors here — a failed
    // addLayer/zoomToLocation (e.g. no real viewer content active yet) is a no-op, not a
    // failure the user needs to act on; the confirmation card below reflects what the backend
    // resolved regardless of whether the viewer itself has real content to render onto yet.
  }, [data.latitude, data.longitude])

  return (
    <Stack spacing={1}>
      <Typography variant="subtitle2">{data.resolvedName ?? 'Site boundary'}</Typography>
      {data.latitude !== null && data.longitude !== null && (
        <Typography variant="body2" color="text.secondary">
          {data.latitude.toFixed(5)}, {data.longitude.toFixed(5)}
        </Typography>
      )}
      <Chip
        size="small"
        label={data.builtAssetConfirmed ? 'Existing physical asset' : 'Planned / proposed site'}
        color={data.builtAssetConfirmed ? 'success' : 'default'}
        sx={{ alignSelf: 'flex-start' }}
      />
    </Stack>
  )
}

panelTypeRegistry.register({
  typeKey: 'site-analysis-boundary',
  renderer: SiteAnalysisBoundaryPanelRenderer,
  schema: siteAnalysisBoundaryDataSchema,
  defaultSize: { width: 320, height: 200 },
  resizable: false,
})
