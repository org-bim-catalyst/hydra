import CloseIcon from '@mui/icons-material/Close'
import VisibilityIcon from '@mui/icons-material/Visibility'
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff'
import { Box, IconButton, Paper, Stack, Tooltip, Typography } from '@mui/material'
import { useSpatialLayersStore, type SpatialLayer } from '../../../store/spatialLayersStore'
import { styleOf } from './SpatialLayersOverlay'

/** A small swatch matching the layer's own look, so the legend and the map never disagree. */
function LayerSwatch({ layer }: { layer: SpatialLayer }) {
  const { color, markerShape } = styleOf(layer)
  const common = { width: 12, height: 12, backgroundColor: color, flex: '0 0 auto' } as const

  if (layer.kind !== 'markers') {
    return <Box sx={{ ...common, borderRadius: '2px', border: `2px solid ${color}`, backgroundColor: 'transparent' }} />
  }

  switch (markerShape) {
    case 'classic-pin':
      return <Box sx={{ ...common, borderRadius: '50% 50% 50% 0', transform: 'rotate(-45deg)' }} />
    case '3d-highlight':
      return <Box sx={{ ...common, width: 14, height: 14, borderRadius: '3px', opacity: 0.75 }} />
    case 'pulsing-ring':
      return (
        <Box
          sx={{
            ...common,
            backgroundColor: 'transparent',
            border: `2px solid ${color}`,
            borderRadius: '50%',
          }}
        />
      )
    default:
      return <Box sx={{ ...common, borderRadius: '50%' }} />
  }
}

function featureCount(layer: SpatialLayer): number {
  return layer.kind === 'markers' ? (layer.markers?.length ?? 0) : 1
}

/**
 * specs/094-spatial-analysis-orchestrator — the floating legend for every spatial layer currently
 * on the map. Renders nothing when no layer is active, so the map stays clean. Each row can be
 * hidden, removed, or the whole set cleared; the overlay renders from the same store, which is
 * what keeps the legend and the map in step.
 */
export function SpatialLayerLegend() {
  const layers = useSpatialLayersStore((s) => s.layers)
  const toggleLayer = useSpatialLayersStore((s) => s.toggleLayer)
  const removeLayer = useSpatialLayersStore((s) => s.removeLayer)
  const clearLayers = useSpatialLayersStore((s) => s.clearLayers)

  if (layers.length === 0) {
    return null
  }

  return (
    <Paper
      elevation={6}
      sx={{
        position: 'absolute',
        right: 12,
        bottom: 76,
        zIndex: 3,
        minWidth: 184,
        maxWidth: 260,
        p: 1.25,
        borderRadius: 2,
        backgroundColor: 'rgba(20, 20, 20, 0.82)',
        color: 'common.white',
        backdropFilter: 'blur(6px)',
      }}
      role="region"
      aria-label="Map layers legend"
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="caption" sx={{ opacity: 0.75, letterSpacing: 0.4 }}>
          MAP LAYERS
        </Typography>
        {layers.length > 1 && (
          <Typography
            component="button"
            type="button"
            variant="caption"
            onClick={clearLayers}
            sx={{
              background: 'none',
              border: 0,
              p: 0,
              cursor: 'pointer',
              color: 'inherit',
              opacity: 0.75,
              textDecoration: 'underline',
              '&:hover': { opacity: 1 },
            }}
          >
            Clear all
          </Typography>
        )}
      </Stack>

      <Stack spacing={0.25}>
        {layers.map((layer) => (
          <Stack
            key={layer.layerId}
            direction="row"
            spacing={1}
            sx={{ alignItems: 'center', py: 0.25, opacity: layer.visible ? 1 : 0.5 }}
          >
            <LayerSwatch layer={layer} />
            <Typography variant="body2" sx={{ flex: 1, lineHeight: 1.2 }} noWrap>
              {layer.legendLabel}
            </Typography>
            <Typography variant="caption" sx={{ opacity: 0.7 }}>
              {featureCount(layer)}
            </Typography>
            <Tooltip title={layer.visible ? `Hide ${layer.legendLabel}` : `Show ${layer.legendLabel}`}>
              <IconButton
                size="small"
                aria-label={layer.visible ? `Hide ${layer.legendLabel}` : `Show ${layer.legendLabel}`}
                onClick={() => toggleLayer(layer.layerId)}
                sx={{ color: 'inherit', opacity: 0.7, p: 0.25, '&:hover': { opacity: 1 } }}
              >
                {layer.visible ? <VisibilityIcon sx={{ fontSize: 14 }} /> : <VisibilityOffIcon sx={{ fontSize: 14 }} />}
              </IconButton>
            </Tooltip>
            <Tooltip title={`Remove ${layer.legendLabel}`}>
              <IconButton
                size="small"
                aria-label={`Remove ${layer.legendLabel}`}
                onClick={() => removeLayer(layer.layerId)}
                sx={{ color: 'inherit', opacity: 0.7, p: 0.25, '&:hover': { opacity: 1 } }}
              >
                <CloseIcon sx={{ fontSize: 14 }} />
              </IconButton>
            </Tooltip>
          </Stack>
        ))}
      </Stack>
    </Paper>
  )
}
