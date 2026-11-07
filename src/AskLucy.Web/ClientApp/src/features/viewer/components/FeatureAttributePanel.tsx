import CloseIcon from '@mui/icons-material/Close'
import {
  Box,
  Divider,
  Drawer,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableRow,
  Tooltip,
  Typography,
} from '@mui/material'
import { type SpatialQueryFeature } from '../../../store/spatialLayersStore'

/** specs/095 — dataset attribute field descriptor; mirrors the backend AttributeField record. */
export interface AttributeFieldDescriptor {
  name: string
  displayName: string
  unit?: string | null
}

export interface FeatureAttributePanelProps {
  /** The feature whose attributes to display; null hides the panel. */
  feature: SpatialQueryFeature | null
  /**
   * Dataset field descriptors used to map raw attribute keys to human-readable labels.
   * When a key is not found, the raw key is shown.
   */
  datasetFields?: AttributeFieldDescriptor[]
  /** Called when the user closes the panel. Must NOT alter layer state. */
  onClose: () => void
}

/**
 * specs/095 (T026) — MUI Drawer panel that shows the attributes of one selected feature from a
 * `feature-query` layer. Opening it does not affect the layer; closing it clears only the
 * selected-feature state (FR-010 / SC-005).
 */
export function FeatureAttributePanel({
  feature,
  datasetFields = [],
  onClose,
}: FeatureAttributePanelProps) {
  const fieldMap = new Map(datasetFields.map((f) => [f.name, f]))

  const resolveLabel = (key: string): string =>
    fieldMap.get(key)?.displayName ?? key

  const resolveUnit = (key: string): string | null =>
    fieldMap.get(key)?.unit ?? null

  const formatValue = (value: string | number | null, unit: string | null): string => {
    if (value === null || value === undefined) return '—'
    return unit ? `${value} ${unit}` : String(value)
  }

  const attributeEntries = feature
    ? Object.entries(feature.attributes).filter(([, v]) => v !== null && v !== '')
    : []

  return (
    <Drawer
      anchor="right"
      open={feature !== null}
      onClose={onClose}
      variant="temporary"
      ModalProps={{ keepMounted: false }}
      slotProps={{
        paper: {
          'aria-label': 'Feature attributes',
          sx: {
            width: { xs: '100%', sm: 340 },
            p: 0,
            backgroundColor: 'background.paper',
          },
        },
      }}
    >
      {/* Header */}
      <Stack
        direction="row"
        sx={{
          alignItems: 'center',
          justifyContent: 'space-between',
          px: 2,
          py: 1.5,
          borderBottom: 1,
          borderColor: 'divider',
        }}
      >
        <Typography variant="subtitle1" noWrap sx={{ flex: 1, mr: 1, fontWeight: 600 }}>
          {feature?.name || 'Feature Details'}
        </Typography>
        <Tooltip title="Close">
          <IconButton
            aria-label="Close feature details"
            onClick={onClose}
            size="small"
            edge="end"
          >
            <CloseIcon fontSize="small" />
          </IconButton>
        </Tooltip>
      </Stack>

      {/* Geometry badge */}
      {feature && (
        <Box sx={{ px: 2, py: 0.75 }}>
          <Typography variant="caption" color="text.secondary" sx={{ textTransform: 'capitalize' }}>
            {feature.geometry.kind} · ID: {feature.featureId}
          </Typography>
        </Box>
      )}

      <Divider />

      {/* Attribute table */}
      {attributeEntries.length > 0 ? (
        <Table size="small" aria-label="Feature attributes table" sx={{ mx: 0 }}>
          <TableBody>
            {attributeEntries.map(([key, value]) => {
              const unit = resolveUnit(key)
              return (
                <TableRow key={key} hover>
                  <TableCell
                    component="th"
                    scope="row"
                    sx={{
                      fontWeight: 500,
                      color: 'text.secondary',
                      width: '45%',
                      whiteSpace: 'nowrap',
                      pl: 2,
                    }}
                  >
                    {resolveLabel(key)}
                  </TableCell>
                  <TableCell sx={{ wordBreak: 'break-word', pr: 2 }}>
                    {formatValue(value, unit)}
                  </TableCell>
                </TableRow>
              )
            })}
          </TableBody>
        </Table>
      ) : (
        <Box sx={{ px: 2, py: 3, textAlign: 'center' }}>
          <Typography variant="body2" color="text.secondary">
            No attribute data available for this feature.
          </Typography>
        </Box>
      )}
    </Drawer>
  )
}
