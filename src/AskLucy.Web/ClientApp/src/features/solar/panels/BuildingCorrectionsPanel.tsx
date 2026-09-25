import { Alert, Box, Button, FormControlLabel, InputBase, Switch, Typography } from '@mui/material'
import { useState } from 'react'
import {
  compactAlertSx,
  compactButtonSx,
  compactInputSx,
  compactLabelSx,
} from '../../../viewer/panels/chrome/compactStyles'
import { copy } from '../copy'
import { useCorrectionsStore } from '../store/correctionsStore'
import { useSolarAnalysisStore } from '../store/solarAnalysisStore'

const sectionTitleSx = { fontSize: 12, fontWeight: 600, lineHeight: 1.4 } as const

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
 *
 * Laid out in the reference page's compact style: small section titles, dim notes, and a narrow
 * monospace input beside a small bordered button on each row.
 */
export function BuildingCorrectionsPanel(): React.JSX.Element | null {
  const site = useSolarAnalysisStore((s) => s.site)
  const siteBuildings = useSolarAnalysisStore((s) => s.siteBuildings)
  const showBuildingMass = useSolarAnalysisStore((s) => s.showBuildingMass)
  const setShowBuildingMass = useSolarAnalysisStore((s) => s.setShowBuildingMass)
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
    <Box sx={{ display: 'flex', flexDirection: 'column', gap: 1.25 }}>
      <Typography sx={compactLabelSx}>{copy.correctionsApplyToSite(site.timeBasisLabel)}</Typography>

      {siteBuilding ? (
        <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
          <Typography variant="subtitle2" sx={sectionTitleSx}>
            {copy.siteBuildingHeightLabel}
          </Typography>
          <Typography sx={compactLabelSx}>
            {siteBuilding.heightProvenance === 'known' ? copy.heightKnown : copy.heightAssumed}
          </Typography>
          <Box sx={{ display: 'flex', gap: 0.75, mt: 0.25 }}>
            <InputBase
              type="number"
              value={heightInput ?? correctedHeight ?? ''}
              onChange={(e) => setHeightInput(e.target.value)}
              slotProps={{ input: { 'aria-label': copy.siteBuildingHeightLabel } }}
              sx={{ ...compactInputSx, flex: 1, minWidth: 0 }}
            />
            <Button size="small" variant="outlined" onClick={applyHeight} sx={compactButtonSx}>
              Apply
            </Button>
          </Box>
          {heightError && (
            <Alert severity="error" sx={compactAlertSx}>
              {heightError}
            </Alert>
          )}
        </Box>
      ) : (
        <Typography sx={compactLabelSx}>{copy.noSiteBuildingFound}</Typography>
      )}

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.5 }}>
        <Typography variant="subtitle2" sx={sectionTitleSx}>
          {copy.groundOffsetLabel}
        </Typography>
        <Box sx={{ display: 'flex', gap: 0.75, mt: 0.25 }}>
          <InputBase
            type="number"
            value={offsetInput ?? groundOffset}
            onChange={(e) => setOffsetInput(e.target.value)}
            slotProps={{ input: { 'aria-label': copy.groundOffsetLabel } }}
            sx={{ ...compactInputSx, flex: 1, minWidth: 0 }}
          />
          <Button size="small" variant="outlined" onClick={applyOffset} sx={compactButtonSx}>
            Set
          </Button>
        </Box>
        {offsetError && (
          <Alert severity="error" sx={compactAlertSx}>
            {offsetError}
          </Alert>
        )}
      </Box>

      <Box sx={{ display: 'flex', flexDirection: 'column', gap: 0.25 }}>
        <FormControlLabel
          control={
            <Switch
              size="small"
              checked={showBuildingMass}
              onChange={(e) => setShowBuildingMass(e.target.checked)}
            />
          }
          label={copy.showBuildingMassLabel}
          slotProps={{ typography: { sx: sectionTitleSx } }}
          sx={{ ml: 0, gap: 0.75 }}
        />
        <Typography sx={compactLabelSx}>{copy.showBuildingMassNote}</Typography>
      </Box>

      <Button
        size="small"
        onClick={() => resetCorrections(site.siteKey)}
        sx={{ ...compactButtonSx, alignSelf: 'flex-start', color: 'text.secondary' }}
      >
        {copy.resetLabel}
      </Button>
    </Box>
  )
}
