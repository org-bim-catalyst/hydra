import { Box, Chip, Tooltip, Typography } from '@mui/material'
import WarningAmberIcon from '@mui/icons-material/WarningAmber'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

interface ProviderHealthCellProps {
  provider: AdminAiProvider
  /** Injectable for deterministic tests; defaults to the real clock. */
  now?: Date
}

type Presentation = {
  label: string
  color: 'success' | 'error' | 'warning' | 'default'
  /** Why the provider is in this state, when there is a reason worth showing. */
  reason?: string | null
}

/**
 * specs/043 US2 — the health column of the AI Providers page.
 *
 * Before this, every non-healthy provider rendered as an identical red "Unhealthy" chip with
 * no reason and a timestamp that could be days old while still reading as current fact. A
 * quota problem, a wrong API key, a disabled billing account and a momentary blip were
 * indistinguishable, which made the page actively misleading rather than merely unhelpful.
 *
 * Presentation precedence follows contracts/admin-provider-health-api.md §1.
 */
export function ProviderHealthCell({ provider, now = new Date() }: ProviderHealthCellProps) {
  const presentation = present(provider)

  // FR-019: computed here, against the current clock, so a page left open turns stale on its
  // own rather than showing a verdict frozen when it was rendered.
  const isStale =
    provider.healthStaleAfterUtc !== null && now.getTime() > new Date(provider.healthStaleAfterUtc).getTime()

  return (
    <Box>
      <Box sx={{ display: 'flex', alignItems: 'center', gap: 0.5, flexWrap: 'wrap' }}>
        <Chip size="small" label={presentation.label} color={presentation.color} variant="outlined" />
        {isStale && (
          <Tooltip title="This result has not been confirmed recently — the background health check may not be running.">
            <Chip
              size="small"
              icon={<WarningAmberIcon fontSize="small" />}
              label="Possibly out of date"
              color="warning"
              variant="outlined"
            />
          </Tooltip>
        )}
      </Box>
      {presentation.reason && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25 }}>
          {presentation.reason}
        </Typography>
      )}
      {provider.healthStatusCheckedAtUtc && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          Checked {new Date(provider.healthStatusCheckedAtUtc).toLocaleString()}
        </Typography>
      )}
    </Box>
  )
}

function present(provider: AdminAiProvider): Presentation {
  // FR-021: nothing has been configured to check, so this is a setup step, not a failure.
  if (!provider.hasCredential) {
    return { label: 'Not configured', color: 'default' }
  }

  // A disabled provider is not checked, so reporting its last known health as current would
  // be misleading in a different direction.
  if (!provider.isEnabled) {
    return { label: 'Not checked while disabled', color: 'default' }
  }

  // FR-020: never red. "We have not looked yet" is not the same claim as "we looked and it
  // is broken".
  if (provider.healthStatus === 'Unknown') {
    return { label: 'Not yet checked', color: 'default' }
  }

  if (provider.healthStatus === 'Healthy') {
    return { label: 'Healthy', color: 'success' }
  }

  // FR-018: a quota or rate limit means the provider is configured correctly and working —
  // it is simply throttled right now. Rendering that in the same red as a rejected credential
  // sends an administrator to change an API key that is perfectly valid.
  if (provider.healthFailureKind === 'QuotaExhausted' || provider.healthFailureKind === 'RateLimited') {
    return {
      label: 'Configured — temporarily limited',
      color: 'warning',
      reason: provider.healthFailureReason,
    }
  }

  return { label: 'Unhealthy', color: 'error', reason: provider.healthFailureReason }
}
