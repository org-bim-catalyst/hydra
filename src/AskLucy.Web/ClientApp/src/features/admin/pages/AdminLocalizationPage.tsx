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
import { useT } from '../../../i18n/useT'
import type { LocalizationSettings } from '../api/adminLocalizationApi'
import { AdminShell } from '../components/AdminShell'
import {
  useLocalizationSettings,
  useUpdateLocalizationSettings,
} from '../hooks/useAdminLocalization'
import { useCanManageNotifications } from '../hooks/useAdminNotifications'
import { useOuterT } from '../hooks/useOuterT'
import { errorText } from '../notificationAdminText'

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
  const t = useT('admin.notifications')
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
          label={t('localization.enable')}
        />
        <FormHelperText sx={{ marginInlineStart: 0 }}>
          {t('localization.enableHelp')}
        </FormHelperText>
      </Box>

      <Box component="fieldset" sx={{ border: 0, p: 0, m: 0 }}>
        <Typography component="legend" variant="subtitle2" sx={{ mb: 0.5 }}>
          {t('localization.supported')}
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
                      {locked ? t('localization.alwaysSupported', { code: language.code }) : <bdi dir="ltr">{language.code}</bdi>}
                    </Typography>
                  </span>
                }
              />
            )
          })}
        </FormGroup>
        <FormHelperText sx={{ marginInlineStart: 0 }}>
          {t('localization.supportedHelp')}
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
            {t('localization.save')}
          </Button>
        </Stack>
      )}
    </Paper>
  )
}

function LocalizationContent() {
  const t = useT('admin.notifications')
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
    <>
      <Stack spacing={2}>
        {update.isError && (
          <Alert
            severity="error"
            action={
              conflict ? (
                <Button color="inherit" size="small" onClick={reload}>
                  {t('actions.reload')}
                </Button>
              ) : undefined
            }
          >
            {conflict
              ? t('localization.conflict', { detail: errorText(t, update.error) })
              : t('localization.saveFailed', { detail: errorText(t, update.error) })}
          </Alert>
        )}

        {settings.isPending ? (
          <Box sx={{ display: 'flex', justifyContent: 'center', py: 4 }}>
            <CircularProgress aria-label={t('localization.loading')} />
          </Box>
        ) : settings.isError ? (
          <Alert
            severity="error"
            action={
              <Button color="inherit" size="small" onClick={() => void settings.refetch()}>
                {t('actions.retry')}
              </Button>
            }
          >
            {errorText(t, settings.error)}
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
        <Alert
          severity="success"
          variant="filled"
          closeText={t('actions.close')}
          onClose={() => setDismissedSavedAt(Date.now())}
        >
          {t('localization.saved')}
        </Alert>
      </Snackbar>
    </>
  )
}

/** specs/067 US8 (FR-044a) — the platform's localization switch and supported languages. Concurrent edits are caught by `If-Match`. */
export function AdminLocalizationPage() {
  const t = useOuterT('admin.notifications')
  return (
    <AdminShell title={t('localization.title')} subtitle={t('localization.subtitle')}>
      <LocalizationContent />
    </AdminShell>
  )
}
