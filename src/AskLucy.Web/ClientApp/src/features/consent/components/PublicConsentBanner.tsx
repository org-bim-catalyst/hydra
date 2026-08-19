import {
  Alert,
  Box,
  Button,
  FormControlLabel,
  Paper,
  Stack,
  Switch,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { COOKIE_CATEGORIES } from '../cookieCategories'
import { useSavePublicCookieConsent } from '../hooks/usePublicCookieConsent'

type ToggleableCategory = 'functional' | 'analytics' | 'marketing'

/**
 * Anonymous-safe companion to `CookieConsentBanner` (research.md Topic 2, contracts/
 * routing-and-consent-contract.md). Unlike the authenticated banner, this is deliberately
 * NON-modal: landing/auth page content stays fully visible and readable behind it — a
 * signed-out visitor evaluating whether to sign up must not be blocked from reading the
 * page by a consent dialog. Reuses the same `COOKIE_CATEGORIES` taxonomy for label/
 * category consistency with the authenticated flow (FR-020).
 */
export function PublicConsentBanner() {
  const saveConsent = useSavePublicCookieConsent()
  const [customizing, setCustomizing] = useState(false)
  const [choices, setChoices] = useState<Record<ToggleableCategory, boolean>>({
    functional: false,
    analytics: false,
    marketing: false,
  })

  const acceptAll = () => saveConsent.mutate({ functional: true, analytics: true, marketing: true })
  const rejectNonEssential = () => saveConsent.mutate({ functional: false, analytics: false, marketing: false })
  const saveCustom = () => saveConsent.mutate(choices)

  return (
    <Paper
      elevation={8}
      role="region"
      aria-label="Cookie preferences"
      sx={{
        position: 'fixed',
        left: 0,
        right: 0,
        bottom: 0,
        zIndex: (theme) => theme.zIndex.snackbar,
        p: { xs: 2, sm: 3 },
        borderTop: '1px solid',
        borderColor: 'divider',
      }}
    >
      <Stack spacing={2} sx={{ maxWidth: 960, mx: 'auto' }}>
        <Typography variant="body2" color="text.secondary">
          Flumeria uses cookies for essential functionality and, with your permission, for functional
          preferences, analytics, and marketing. Read our{' '}
          <RouterLink to="/privacy" target="_blank" rel="noopener">
            Privacy Policy
          </RouterLink>{' '}
          for full details.
        </Typography>

        {saveConsent.isError && <Alert severity="error">Couldn't save your cookie preferences. Please try again.</Alert>}

        {customizing && (
          <Stack spacing={1.5}>
            {COOKIE_CATEGORIES.map((category) => (
              <Stack key={category.key} direction="row" sx={{ alignItems: 'flex-start' }} spacing={1}>
                <FormControlLabel
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
              </Stack>
            ))}
          </Stack>
        )}

        <Box sx={{ display: 'flex', gap: 1.5, flexWrap: 'wrap', justifyContent: { xs: 'stretch', sm: 'flex-end' } }}>
          {customizing ? (
            <Button variant="contained" onClick={saveCustom} disabled={saveConsent.isPending}>
              Save preferences
            </Button>
          ) : (
            <>
              <Button onClick={() => setCustomizing(true)} disabled={saveConsent.isPending}>
                Customize
              </Button>
              <Button onClick={rejectNonEssential} disabled={saveConsent.isPending}>
                Reject Non-Essential
              </Button>
              <Button variant="contained" onClick={acceptAll} disabled={saveConsent.isPending}>
                Accept All
              </Button>
            </>
          )}
        </Box>
      </Stack>
    </Paper>
  )
}
