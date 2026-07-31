import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  FormControlLabel,
  Stack,
  Switch,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { Link as RouterLink } from 'react-router'
import { COOKIE_CATEGORIES } from '../cookieCategories'
import { useSaveCookieConsent } from '../hooks/useCookieConsent'

type ToggleableCategory = 'functional' | 'analytics' | 'marketing'

/**
 * Blocking, non-dismissible consent banner (spec.md FR-020): no `onClose` handler and
 * `disableEscapeKeyDown` mean the only way out is one of the three explicit actions below.
 * Rendered by `ConsentGate` on top of the main app page, not as a separate route, matching
 * spec.md's own "banner appears in the main page" framing.
 */
export function CookieConsentBanner() {
  const saveConsent = useSaveCookieConsent()
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
    <Dialog
      open
      onClose={() => {
        /* non-dismissible — `open` stays true regardless of the close reason (escape key,
           backdrop click), so the only way out is Accept All, Reject Non-Essential, or
           Customize+Save (MUI v9 removed the old `disableEscapeKeyDown` prop; leaving
           `open` uncontrolled by `onClose` already achieves the same non-dismissible result) */
      }}
      aria-labelledby="cookie-consent-title"
      maxWidth="sm"
      fullWidth
    >
      <DialogTitle id="cookie-consent-title">We use cookies</DialogTitle>
      <DialogContent>
        <DialogContentText sx={{ mb: 2 }}>
          Ask Lucy uses cookies for essential functionality and, with your permission, for functional
          preferences, analytics, and marketing. Read our{' '}
          <RouterLink to="/privacy" target="_blank" rel="noopener">
            Privacy Policy
          </RouterLink>{' '}
          for full details.
        </DialogContentText>

        {saveConsent.isError && (
          <Alert severity="error" sx={{ mb: 2 }}>
            Couldn't save your cookie preferences. Please try again.
          </Alert>
        )}

        {customizing && (
          <Stack spacing={1.5} sx={{ mb: 1 }}>
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
      </DialogContent>
      <DialogActions sx={{ px: 3, pb: 2 }}>
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
      </DialogActions>
    </Dialog>
  )
}
