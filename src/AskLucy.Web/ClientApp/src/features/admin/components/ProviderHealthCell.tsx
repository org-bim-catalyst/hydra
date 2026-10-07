import { Box, Chip, Typography } from '@mui/material'
import { useFormat, useT } from '../../../i18n/useT'
import type { Translate } from '../../../i18n/useT'
import type { AdminAiProvider } from '../api/adminAiProvidersApi'

interface ProviderHealthCellProps {
  provider: AdminAiProvider
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
 *
 * The "possibly out of date" staleness indicator lives in its own column — see
 * ProviderStalenessCell (specs/062 US1).
 */
export function ProviderHealthCell({ provider }: ProviderHealthCellProps) {
  const t = useT('admin.aiProviders')
  const format = useFormat()
  const presentation = present(provider, t)

  return (
    <Box>
      <Chip size="small" label={presentation.label} color={presentation.color} variant="outlined" />
      {presentation.reason && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25 }}>
          {presentation.reason}
        </Typography>
      )}
      {provider.healthStatusCheckedAtUtc && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {t('health.checked', {
            when: format.date(provider.healthStatusCheckedAtUtc, {
              year: 'numeric',
              month: 'numeric',
              day: 'numeric',
              hour: 'numeric',
              minute: 'numeric',
              second: 'numeric',
            }),
          })}
        </Typography>
      )}
    </Box>
  )
}

function present(provider: AdminAiProvider, t: Translate<'admin.aiProviders'>): Presentation {
  // FR-021: nothing has been configured to check, so this is a setup step, not a failure.
  if (!provider.hasCredential) {
    return { label: t('health.notConfigured'), color: 'default' }
  }

  // A disabled provider is not checked, so reporting its last known health as current would
  // be misleading in a different direction.
  if (!provider.isEnabled) {
    return { label: t('health.notCheckedWhileDisabled'), color: 'default' }
  }

  // FR-020: never red. "We have not looked yet" is not the same claim as "we looked and it
  // is broken".
  if (provider.healthStatus === 'Unknown') {
    return { label: t('health.notYetChecked'), color: 'default' }
  }

  if (provider.healthStatus === 'Healthy') {
    return { label: t('health.healthy'), color: 'success' }
  }

  // FR-018: a quota or rate limit means the provider is configured correctly and working —
  // it is simply throttled right now. Rendering that in the same red as a rejected credential
  // sends an administrator to change an API key that is perfectly valid.
  if (provider.healthFailureKind === 'QuotaExhausted' || provider.healthFailureKind === 'RateLimited') {
    return {
      label: t('health.temporarilyLimited'),
      color: 'warning',
      reason: provider.healthFailureReason,
    }
  }

  return { label: t('health.unhealthy'), color: 'error', reason: provider.healthFailureReason }
}
