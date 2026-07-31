import { Alert, Box, Button, CircularProgress, FormControlLabel, Stack, Switch, Typography } from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import type { CookieConsentStatus } from '../api/consentApi'
import { COOKIE_CATEGORIES } from '../cookieCategories'
import { useCookieConsent, useSaveCookieConsent } from '../hooks/useCookieConsent'

type ToggleableCategory = 'functional' | 'analytics' | 'marketing'

function toChoices(status: CookieConsentStatus): Record<ToggleableCategory, boolean> {
  return { functional: status.functional, analytics: status.analytics, marketing: status.marketing }
}

/**
 * Owns the editable toggle state, lazily initialized from the loaded status. Keyed by
 * `data.lastUpdatedAtUtc` in the parent below, so a successful save (which changes that
 * timestamp) remounts this with fresh initial state instead of syncing via an effect.
 */
function CookiePreferencesForm({ data }: { data: CookieConsentStatus }) {
  const saveConsent = useSaveCookieConsent()
  const [choices, setChoices] = useState(() => toChoices(data))

  return (
    <Stack spacing={3} sx={{ maxWidth: 480 }}>
      <Typography variant="h6">Cookie preferences</Typography>

      {data.lastUpdatedAtUtc && (
        <Typography variant="body2" color="text.secondary">
          Last updated: {new Date(data.lastUpdatedAtUtc).toLocaleString()}
        </Typography>
      )}

      {saveConsent.isError && <Alert severity="error">Couldn't save your cookie preferences. Please try again.</Alert>}
      {saveConsent.isSuccess && <Alert severity="success">Preferences saved.</Alert>}

      <Stack spacing={1.5}>
        {COOKIE_CATEGORIES.map((category) => (
          <FormControlLabel
            key={category.key}
            control={
              category.locked ? (
                <Switch checked disabled />
              ) : (
                <Switch
                  checked={choices[category.key as ToggleableCategory]}
                  onChange={(_, checked) =>
                    setChoices((prev) => ({ ...prev, [category.key as ToggleableCategory]: checked }))
                  }
                />
              )
            }
            label={
              <Stack>
                <Typography variant="body2" sx={{ fontWeight: 600 }}>
                  {category.label}
                </Typography>
                <Typography variant="caption" color="text.secondary">
                  {category.description}
                </Typography>
              </Stack>
            }
            sx={{ alignItems: 'flex-start', ml: 0 }}
          />
        ))}
      </Stack>

      <Button
        variant="contained"
        onClick={() => saveConsent.mutate(choices)}
        disabled={saveConsent.isPending}
        sx={{ alignSelf: 'flex-start' }}
      >
        Save preferences
      </Button>

      <Typography variant="body2">
        Read our{' '}
        <RouterLink to="/privacy" target="_blank" rel="noopener">
          Privacy Policy
        </RouterLink>{' '}
        for full details.
      </Typography>
    </Stack>
  )
}

/** Rendered as the "Cookies" tab in Settings (spec.md FR-011/012/013 — a Settings section, not a separate route). */
export function CookiePreferencesPanel() {
  const { data, isPending, isError, refetch } = useCookieConsent()

  if (isPending) {
    return (
      <Box sx={{ display: 'flex', justifyContent: 'center', p: 4 }}>
        <CircularProgress role="status" aria-live="polite" aria-label="Loading cookie preferences…" />
      </Box>
    )
  }

  if (isError || !data) {
    return (
      <Stack spacing={2} sx={{ alignItems: 'flex-start' }}>
        <Alert severity="error" role="alert">
          Couldn't load your cookie preferences. Please try again.
        </Alert>
        <Button variant="outlined" onClick={() => void refetch()}>
          Retry
        </Button>
      </Stack>
    )
  }

  return <CookiePreferencesForm key={data.lastUpdatedAtUtc ?? 'never'} data={data} />
}
