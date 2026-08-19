import { Alert, Box, Slider, Stack, Typography } from '@mui/material'
import { useEffect } from 'react'
import { usePanelPreferencesStore } from '../../../viewer/panels/store/panelPreferencesStore'

const MIN_OPACITY_PERCENT = 40
const MAX_OPACITY_PERCENT = 100

/**
 * specs/028-ai-floating-panels FR-011/FR-012 (User Story 3). Every change auto-persists via
 * `panelPreferencesStore.update()` — same immediate-persist convention as `VoiceTab`'s per-field
 * toggles, since this is a single, lightweight control rather than a provider+model pair that
 * only makes sense saved together.
 */
export function ViewerTab() {
  const opacityPercent = usePanelPreferencesStore((s) => s.opacityPercent)
  const update = usePanelPreferencesStore((s) => s.update)
  const error = usePanelPreferencesStore((s) => s.error)
  const clearError = usePanelPreferencesStore((s) => s.clearError)
  const hydrateFromServer = usePanelPreferencesStore((s) => s.hydrateFromServer)

  useEffect(() => {
    void hydrateFromServer()
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  return (
    <Stack spacing={4}>
      {error && (
        <Alert severity="error" sx={{ maxWidth: 480 }} onClose={clearError}>
          {error}
        </Alert>
      )}

      <Box>
        <Typography variant="h6" sx={{ mb: 1 }}>
          Floating panel opacity
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
          Controls how transparent AI-generated floating panels are over the viewer. Applies to
          every open panel immediately.
        </Typography>
        <Box sx={{ maxWidth: 480 }}>
          <Slider
            aria-label="Panel opacity"
            min={MIN_OPACITY_PERCENT}
            max={MAX_OPACITY_PERCENT}
            step={1}
            value={opacityPercent}
            valueLabelDisplay="auto"
            valueLabelFormat={(value) => `${value}%`}
            onChange={(_, value) => void update({ opacityPercent: value as number })}
          />
        </Box>
      </Box>
    </Stack>
  )
}
