import CloseIcon from '@mui/icons-material/Close'
import { Box, IconButton, Paper, Stack, Tooltip, Typography } from '@mui/material'
import { usePoiLayersStore, type PoiLayer } from '../../../store/poiLayersStore'

/** A small shape swatch matching the marker the layer draws, in the layer's own colour. */
function LayerSwatch({ layer }: { layer: PoiLayer }) {
  const common = { width: 12, height: 12, backgroundColor: layer.color, flex: '0 0 auto' } as const

  switch (layer.markerShape) {
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
            border: `2px solid ${layer.color}`,
            borderRadius: '50%',
          }}
        />
      )
    default:
      return <Box sx={{ ...common, borderRadius: '50%' }} />
  }
}

/**
 * specs/090-poi-discovery — the floating legend for the POI layers currently on the map.
 * Renders nothing when no layer is active, so the map stays clean. Each row is one category with
 * its marker style and count; removing a row removes that category's markers in the same store
 * update, which is what keeps the legend and the map in step.
 */
export function PoiLayerLegend() {
  const layers = usePoiLayersStore((s) => s.layers)
  const removeLayer = usePoiLayersStore((s) => s.removeLayer)
  const clearLayers = usePoiLayersStore((s) => s.clearLayers)

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
        minWidth: 168,
        maxWidth: 240,
        p: 1.25,
        borderRadius: 2,
        backgroundColor: 'rgba(20, 20, 20, 0.82)',
        color: 'common.white',
        backdropFilter: 'blur(6px)',
      }}
      role="region"
      aria-label="Point of interest legend"
    >
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 0.5 }}>
        <Typography variant="caption" sx={{ opacity: 0.75, letterSpacing: 0.4 }}>
          POI LAYERS
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
            key={layer.category}
            direction="row"
            spacing={1}
            sx={{ alignItems: 'center', py: 0.25 }}
          >
            <LayerSwatch layer={layer} />
            <Typography variant="body2" sx={{ flex: 1, lineHeight: 1.2 }} noWrap>
              {layer.displayName}
            </Typography>
            <Typography variant="caption" sx={{ opacity: 0.7 }}>
              {layer.markers.length}
            </Typography>
            <Tooltip title={`Remove ${layer.displayName}`}>
              <IconButton
                size="small"
                aria-label={`Remove ${layer.displayName}`}
                onClick={() => removeLayer(layer.category)}
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
