import {
  Alert,
  Box,
  Button,
  Checkbox,
  CircularProgress,
  FormControlLabel,
  FormGroup,
  FormHelperText,
  Paper,
  Snackbar,
  Stack,
  Switch,
  Typography,
} from '@mui/material'
import { useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import type { LocalizationSettings } from '../api/adminLocalizationApi'
import { AdminShell } from '../components/AdminShell'
import {
  useLocalizationSettings,
  useUpdateLocalizationSettings,
} from '../hooks/useAdminLocalization'
import { useCanManageNotifications } from '../hooks/useAdminNotifications'

const errorMessage = (err: unknown) =>
  err instanceof ApiError ? (err.detail ?? err.message) : 'Something went wrong. Please try again.'

const sameSet = (a: string[], b: string[]) =>
  a.length === b.length && a.every((code) => b.includes(code))

interface FormProps {
  settings: LocalizationSettings
  canManage: boolean
  isSaving: boolean
  onSave: (draft: { isEnabled: boolean; supportedLanguages: string[] }) => void
}

/** Keyed by the row version by the page, so a reload (or a save) starts the draft again from what is stored. */
function LocalizationForm({ settings, canManage, isSaving, onSave }: FormProps) {
  const [isEnabled, setIsEnabled] = useState(settings.isEnabled)
  const [supported, setSupported] = useState<string[]>(settings.supportedLanguages)

  const dirty = isEnabled !== settings.isEnabled || !sameSet(supported, settings.supportedLanguages)

  const toggleLanguage = (code: string, checked: boolean) =>
    setSupported((current) => (checked ? [...current, code] : current.filter((c) => c !== code)))

  return (
    <Paper elevation={1} sx={{ p: 2, display: 'flex', flexDirection: 'column', gap: 2 }}>
      <Box>
        <FormControlLabel
          control={
            <Switch
              checked={isEnabled}
              disabled={!canManage || isSaving}
              onChange={(e) => setIsEnabled(e.target.checked)}
            />
          }
          label="Enable localization"
        />
        <FormHelperText sx={{ marginInlineStart: 0 }}>
          While off, every notification, email and screen is in English and users are not offered a
          language choice. Users keep the language they chose, and get it back when localization is
          turned on again.
        </FormHelperText>
      </Box>

      <Box component="fieldset" sx={{ border: 0, p: 0, m: 0 }}>
        <Typography component="legend" variant="subtitle2" sx={{ mb: 0.5 }}>
          Supported languages
        </Typography>
        <FormGroup>
          {settings.availableLanguages.map((language) => {
            const locked = language.locked || language.code === 'en'
            return (
              <FormControlLabel
                key={language.code}
                control={
                  <Checkbox
                    checked={locked || supported.includes(language.code)}
                    disabled={locked || !canManage || isSaving}
                    onChange={(e) => toggleLanguage(language.code, e.target.checked)}
                  />
                }
                label={
                  <span>
                    <span lang={language.code}>{language.nativeName}</span>
                    <Typography
                      component="span"
                      variant="caption"
                      color="text.secondary"
                      sx={{ marginInlineStart: 1 }}
                    >
                      {locked ? `${language.code} · always supported` : language.code}
                    </Typography>
                  </span>
                }
              />
            )
          })}
        </FormGroup>
        <FormHelperText sx={{ marginInlineStart: 0 }}>
          Only languages the platform ships content for are listed. A user whose language is removed
          falls back to English.
        </FormHelperText>
      </Box>

      {canManage && (
        <Stack direction="row">
          <Button
            variant="contained"
            disabled={!dirty || isSaving}
            onClick={() =>
              onSave({ isEnabled, supportedLanguages: Array.from(new Set(['en', ...supported])) })
            }
          >
            Save
          </Button>
        </Stack>
      )}
    </Paper>
  )
}

/** specs/067 US8 (FR-044a) — the platform's localization switch and supported languages. Concurrent edits are caught by `If-Match`. */
export function AdminLocalizationPage() {
  const canManage = useCanManageNotifications()
  const settings = useLocalizationSettings()
  const update = useUpdateLocalizationSettings()
  const [dismissedSavedAt, setDismissedSavedAt] = useState<number | null>(null)

  const conflict = update.error instanceof ApiError && update.error.status === 409

  const reload = () => {
    update.reset()
    void settings.refetch()
  }

  return (
    <AdminShell
      title="Localization"
      subtitle="Which languages users can choose, and whether localization is on"
    >
      <Stack spacing={2}>
        {update.isError && (
          <Alert
            severity="error"
            action={
              conflict ? (
                <Button color="inherit" size="small" onClick={reload}>
                  Reload
                </Button>
              ) : undefined
            }
          >
            {conflict
              ? `The settings were changed by someone else. Reload to see the current settings, then make your change again. ${errorMessage(update.error)}`
              : `The settings weren't saved. ${errorMessage(update.error)}`}
          </Alert>
        )}

        {settings.isPending ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
            <CircularProgress aria-label="Loading localization settings" />
          </Box>
        ) : settings.isError ? (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void settings.refetch()}>
                Retry
              </Button>
            }
          >
            {errorMessage(settings.error)}
          </Alert>
        ) : (
          <LocalizationForm
            key={settings.data.rowVersion}
            settings={settings.data}
            canManage={canManage}
            isSaving={update.isPending}
            onSave={(draft) => {
              setDismissedSavedAt(null)
              update.mutate({ rowVersion: settings.data.rowVersion, settings: draft })
            }}
          />
        )}
      </Stack>

      <Snackbar
        open={update.isSuccess && dismissedSavedAt === null}
        autoHideDuration={5000}
        onClose={() => setDismissedSavedAt(Date.now())}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
      >
        <Alert severity="success" variant="filled" onClose={() => setDismissedSavedAt(Date.now())}>
          Localization settings saved.
        </Alert>
      </Snackbar>
    </AdminShell>
  )
}
