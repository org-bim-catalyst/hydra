import {
  Alert,
  Box,
  Button,
  Snackbar,
  ToggleButton,
  ToggleButtonGroup,
  Typography,
} from '@mui/material'
import { useMutationState } from '@tanstack/react-query'
import { useState } from 'react'
import {
  canSwitchLanguage,
  SET_LANGUAGE_MUTATION_KEY,
  useLocalization,
  useSetLanguage,
} from '../../../i18n/useLocalization'
import { useT } from '../../../i18n/useT'
import { errorMessage } from '../../notifications/api/errorMessage'

/**
 * specs/067 US8 (FR-044b) — the caller's own language, chosen from the supported languages. Offered only while
 * localization is on and more than one language is supported; otherwise it renders nothing. Choosing one calls
 * `PUT /users/me/localization`, which invalidates `['localization']`, so every `LocalizedSurface` re-renders in the
 * new language and direction without a reload. A failure is reported by `LanguageSwitchErrorToast`.
 */
export function LanguageSwitch() {
  const t = useT('notifications')
  const { data } = useLocalization()
  const setLanguage = useSetLanguage()

  if (!canSwitchLanguage(data)) return null

  return (
    <Box
      sx={{
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: 1.5,
        px: 1.5,
        py: 1,
      }}
    >
      <Typography id="language-switch-label" variant="body2" sx={{ fontWeight: 500 }}>
        {t('language.label')}
      </Typography>
      <ToggleButtonGroup
        size="small"
        exclusive
        value={data.effectiveLanguage}
        disabled={setLanguage.isPending}
        aria-labelledby="language-switch-label"
        onChange={(_, code: string | null) => {
          if (code !== null && code !== data.effectiveLanguage) setLanguage.mutate(code)
        }}
      >
        {data.supportedLanguages.map((language) => (
          // Each name is in its own language, so it carries its own `lang` (and a screen reader pronounces it correctly).
          <ToggleButton
            key={language.code}
            value={language.code}
            lang={language.code}
            sx={{ textTransform: 'none', px: 1.25, py: 0.25 }}
          >
            {language.nativeName}
          </ToggleButton>
        ))}
      </ToggleButtonGroup>
    </Box>
  )
}

/**
 * The error toast, with a retry, for a failed language change. It reads the mutation cache rather than the switch's
 * own state so that it survives the account menu closing (and unmounting the switch) while the request is in flight.
 * Mount it once, beside the menu, wherever `LanguageSwitch` is offered.
 */
export function LanguageSwitchErrorToast() {
  const t = useT('notifications')
  const tc = useT('common')
  const retry = useSetLanguage()
  const [dismissed, setDismissed] = useState<number | null>(null)
  const attempts = useMutationState({
    filters: { mutationKey: SET_LANGUAGE_MUTATION_KEY },
    select: (mutation) => ({
      status: mutation.state.status,
      error: mutation.state.error,
      variables: mutation.state.variables as string | undefined,
      submittedAt: mutation.state.submittedAt,
    }),
  })
  const latest = attempts.reduce<(typeof attempts)[number] | undefined>(
    (best, attempt) =>
      best === undefined || attempt.submittedAt > best.submittedAt ? attempt : best,
    undefined,
  )
  const failed = latest?.status === 'error' && latest.submittedAt !== dismissed

  return (
    <Snackbar
      open={failed}
      autoHideDuration={10000}
      onClose={(_, reason) => {
        if (reason !== 'clickaway' && latest) setDismissed(latest.submittedAt)
      }}
      anchorOrigin={{ vertical: 'bottom', horizontal: 'center' }}
    >
      <Alert
        severity="error"
        variant="filled"
        onClose={() => latest && setDismissed(latest.submittedAt)}
        action={
          latest?.variables ? (
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                if (latest.variables) retry.mutate(latest.variables)
              }}
            >
              {tc('actions.retry')}
            </Button>
          ) : undefined
        }
      >
        {t('language.changeFailed', { detail: errorMessage(latest?.error, tc('errors.generic')) })}
      </Alert>
    </Snackbar>
  )
}
