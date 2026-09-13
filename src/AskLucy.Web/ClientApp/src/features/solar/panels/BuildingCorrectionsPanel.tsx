import { Alert, Box, Button, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { z } from 'zod'
import { copy } from '../copy'
import { useCorrectionsStore } from '../store/correctionsStore'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

export const SOLAR_CORRECTIONS_TYPE_KEY = 'solar.corrections'

export const solarCorrectionsDataSchema = z.object({})
export type SolarCorrectionsData = z.infer<typeof solarCorrectionsDataSchema>

/**
 * contracts/solar-panels.md "Building Corrections" — shows the site building's height AND
 * whether it was `known` or `assumed` (FR-011), height correction (FR-025), ground offset
 * (FR-026), the site label these corrections apply to (FR-028), and a reset. Driven entirely by
 * `solarAnalysisStore` (for the site building and its recorded height) and `correctionsStore`
 * (for the session-scoped override) — the same store split the time control panel uses, and for
 * the same reason (research D12).
 *
 * Validation (FR-027): an invalid entry is rejected with a stated reason and the PREVIOUS value
 * is kept — never a silent clamp, never a discarded edit. The rejection message is shown inline
 * and cleared the next time the field is edited.
 */
export function BuildingCorrectionsPanel(): React.JSX.Element | null {
  const site = useSolarAnalysisStore((s) => s.site)
  const siteBuildings = useSolarAnalysisStore((s) => s.siteBuildings)
  const setBuildingHeight = useCorrectionsStore((s) => s.setBuildingHeight)
  const setGroundOffset = useCorrectionsStore((s) => s.setGroundOffset)
  const resetCorrections = useCorrectionsStore((s) => s.reset)
  const corrections = useCorrectionsStore((s) => (site ? s.bySiteKey[site.siteKey] : undefined))

  const [heightInput, setHeightInput] = useState<string | null>(null)
  const [heightError, setHeightError] = useState<string | null>(null)
  const [offsetInput, setOffsetInput] = useState<string | null>(null)
  const [offsetError, setOffsetError] = useState<string | null>(null)

  if (!site) return null

  const siteBuilding = siteBuildings.find((b) => b.isSiteBuilding)
  const correctedHeight = siteBuilding ? (corrections?.buildingHeights[siteBuilding.id] ?? siteBuilding.heightMetres) : null
  const groundOffset = corrections?.groundOffsetMetres ?? 0

  function applyHeight() {
    if (!siteBuilding || heightInput === null) return
    const parsed = Number(heightInput)
    const result = setBuildingHeight(site!.siteKey, siteBuilding.id, parsed)
    if (!result.ok) {
      setHeightError(result.reason)
    } else {
      setHeightError(null)
      setHeightInput(null)
    }
  }

  function applyOffset() {
    if (offsetInput === null) return
    const parsed = Number(offsetInput)
    const result = setGroundOffset(site!.siteKey, parsed)
    if (!result.ok) {
      setOffsetError(result.reason)
    } else {
      setOffsetError(null)
      setOffsetInput(null)
    }
  }

  return (
    <Box sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Typography variant="body2" color="text.secondary">
        {copy.correctionsApplyToSite(site.timeBasisLabel)}
      </Typography>

      {siteBuilding ? (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
          <Typography variant="subtitle2">{copy.siteBuildingHeightLabel}</Typography>
          <Typography variant="caption" color="text.secondary">
            {siteBuilding.heightProvenance === 'known' ? copy.heightKnown : copy.heightAssumed}
          </Typography>
          <Box sx={{ display: 'flex', flexDirection: 'row', gap: 1 }}>
            <TextField
              type="number"
              size="small"
              value={heightInput ?? correctedHeight ?? ''}
              onChange={(e) => setHeightInput(e.target.value)}
              slotProps={{ htmlInput: { 'aria-label': copy.siteBuildingHeightLabel } }}
            />
            <Button size="small" variant="outlined" onClick={applyHeight}>
              Apply
            </Button>
          </Box>
          {heightError && <Alert severity="error">{heightError}</Alert>}
        </Box>
      ) : (
        <Typography variant="caption" color="text.secondary">
          {copy.noSiteBuildingFound}
        </Typography>
      )}

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
        <Typography variant="subtitle2">{copy.groundOffsetLabel}</Typography>
        <Box sx={{ display: 'flex', flexDirection: 'row', gap: 1 }}>
          <TextField
            type="number"
            size="small"
            value={offsetInput ?? groundOffset}
            onChange={(e) => setOffsetInput(e.target.value)}
            slotProps={{ htmlInput: { 'aria-label': copy.groundOffsetLabel } }}
          />
          <Button size="small" variant="outlined" onClick={applyOffset}>
            Set
          </Button>
        </Box>
        {offsetError && <Alert severity="error">{offsetError}</Alert>}
      </Box>

      <Button size="small" onClick={() => resetCorrections(site.siteKey)}>
        {copy.resetLabel}
      </Button>
    </Box>
  )
}
