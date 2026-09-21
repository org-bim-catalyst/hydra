import { Chip, Tooltip } from '@mui/material'
import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

interface ProviderStalenessCellProps {
  provider: AdminAiProvider
  /** Injectable for deterministic tests; defaults to the real clock. */
  now?: Date
}

/**
 * specs/062 US1 — staleness indicator, split out of ProviderHealthCell into its own column
 * so it no longer wraps inline with the health status chip and its tooltip no longer
 * overlaps the row below.
 */
export function ProviderStalenessCell({ provider, now = new Date() }: ProviderStalenessCellProps) {
  // FR-019: computed here, against the current clock, so a page left open turns stale on its
  // own rather than showing a verdict frozen when it was rendered.
  const isStale =
    provider.healthStaleAfterUtc !== null && now.getTime() > new Date(provider.healthStaleAfterUtc).getTime()

  if (!isStale) {
    return null
  }

  return (
    <Tooltip title="This result has not been confirmed recently — the background health check may not be running.">
      <Chip
        size="small"
        icon={<WarningAmberIcon fontSize="small" />}
        label="Possibly out of date"
        color="warning"
        variant="outlined"
      />
    </Tooltip>
  )
}
