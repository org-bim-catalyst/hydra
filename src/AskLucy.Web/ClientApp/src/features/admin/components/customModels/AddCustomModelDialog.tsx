import { useEffect, useMemo, useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  Snackbar,
  Stack,
  TextField,
} from '@mui/material'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useForm, useWatch } from 'react-hook-form'
import type { UseFormRegisterReturn } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../../../../api/httpClient'
import { useT } from '../../../../i18n/useT'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { errorMessage } from './errorMessage'
import type { Translate } from '../../../../i18n/useT'

interface AddCustomModelDialogProps {
  open: boolean
  onClose: () => void
  onSubmitted?: (model: CustomModelSummary) => void
}

interface AddCustomModelFormValues {
  source: string
  destination: string
  name: string
}

const FIELDS = ['source', 'destination', 'name'] as const
const PREVIEW_DEBOUNCE_MS = 400

// Validated with Zod through react-hook-form's `validate` hook rather than a resolver package, as in
// ForgotPasswordPage. The server re-validates everything; these only catch the obvious locally. The schemas are
// built per language so their messages are the translated ones.
function buildSchemas(t: Translate<'admin.aiProviders'>) {
  return {
    source: z
      .string()
      .trim()
      .min(1, t('add.validation.sourceRequired'))
      .max(2048, t('add.validation.sourceTooLong')),
    destination: z
      .string()
      .trim()
      .min(1, t('add.validation.destinationRequired'))
      .max(512, t('add.validation.destinationTooLong')),
    name: z
      .string()
      .trim()
      .min(1, t('add.validation.nameRequired'))
      .regex(/^[\p{L}\p{N}][\p{L}\p{N} ._-]{0,99}$/u, t('add.validation.nameInvalid')),
  }
}

const validateWith = (schema: z.ZodType<string>) => (value: string) => {
  const result = schema.safeParse(value)
  return result.success || result.error.issues[0].message
}

/** MUI's TextField puts `ref` on its root; react-hook-form needs the input itself for focus. */
function fieldProps({ ref, ...rest }: UseFormRegisterReturn) {
  return { ...rest, inputRef: ref }
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debounced, setDebounced] = useState(value)
  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs)
    return () => clearTimeout(timer)
  }, [value, delayMs])
  return debounced
}

/**
 * specs/072 US1 — asks for a Hugging Face repository and a destination folder under the deployment
 * root. The server downloads and uploads it; nothing passes through this browser. The Name field
 * appears only when the source's own name can't be used.
 */
export function AddCustomModelDialog({ open, onClose, onSubmitted }: AddCustomModelDialogProps) {
  const t = useT('admin.aiProviders')
  const schemas = useMemo(() => buildSchemas(t), [t])
  const queryClient = useQueryClient()
  const [toast, setToast] = useState<string | null>(null)
  const [nameForced, setNameForced] = useState(false)

  const form = useForm<AddCustomModelFormValues>({
    defaultValues: { source: '', destination: '', name: '' },
    shouldUnregister: true,
  })
  const { errors } = form.formState

  const source = useWatch({ control: form.control, name: 'source' }) ?? ''
  const debouncedSource = useDebouncedValue(source.trim(), PREVIEW_DEBOUNCE_MS)

  const statusQuery = useQuery({
    queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.deploymentStatus,
    queryFn: customModelsApi.getDeploymentStatus,
    enabled: open,
  })
  const status = statusQuery.data

  const previewQuery = useQuery({
    queryKey: [...customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all, 'source-preview', debouncedSource],
    queryFn: () => customModelsApi.previewCustomModelSource(debouncedSource),
    enabled: open && debouncedSource.length > 0,
  })
  // Only trust a preview that matches what is in the box now, not one for an earlier keystroke.
  const preview = debouncedSource === source.trim() ? previewQuery.data : undefined

  const showName =
    nameForced ||
    (preview?.isValid === true && (preview.derivedName === null || !preview.nameAvailable))

  const submitMutation = useMutation({
    mutationFn: (values: AddCustomModelFormValues) =>
      customModelsApi.submitCustomModel({
        source: values.source.trim(),
        destination: values.destination.trim(),
        ...(showName ? { name: values.name.trim() } : {}),
      }),
    onSuccess: (model) => {
      void queryClient.invalidateQueries({ queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.all })
      onSubmitted?.(model)
      close()
    },
    onError: (err) => {
      const fieldErrors = err instanceof ApiError ? err.errors : undefined
      let placed = false
      for (const field of FIELDS) {
        const message = fieldErrors?.[field]?.[0]
        if (!message) continue
        if (field === 'name') setNameForced(true)
        form.setError(field, { type: 'server', message })
        placed = true
      }
      if (!placed) setToast(errorMessage(err, t))
    },
  })

  function close() {
    form.reset()
    setNameForced(false)
    submitMutation.reset()
    onClose()
  }

  const onSubmit = form.handleSubmit((values) => submitMutation.mutate(values))

  const notConfigured = status?.isConfigured === false
  const sourceHelper =
    errors.source?.message ??
    (preview && !preview.isValid ? preview.error : undefined) ??
    (preview?.repositoryId
      ? t('add.sourceResolved', {
          repository: preview.repositoryId,
          revision: preview.revision ?? 'main',
        })
      : t('add.sourceExample', { example: 'https://huggingface.co/Supertone/supertonic-3' }))
  const prefixes = status?.allowedDestinationPrefixes ?? []
  const destinationHelper =
    errors.destination?.message ??
    (prefixes.length > 0
      ? t('add.destinationHelperPrefixes', {
          prefixes: prefixes.map((p) => `${p}/`).join(t('add.prefixSeparator')),
        })
      : t('add.destinationHelper'))

  return (
    <>
      <Dialog open={open} onClose={close} maxWidth="sm" fullWidth>
        <Box component="form" onSubmit={onSubmit} noValidate>
          <DialogTitle>{t('add.title')}</DialogTitle>
          <DialogContent>
            <DialogContentText sx={{ mb: 2 }}>
              {t('add.description', { resolve: '/resolve/', blob: '/blob/' })}
            </DialogContentText>
            <Stack spacing={2}>
              {statusQuery.isError && (
                <Alert
                  severity="error"
                  action={
                    <Button color="inherit" size="small" onClick={() => void statusQuery.refetch()}>
                      {t('shared.retry')}
                    </Button>
                  }
                >
                  {errorMessage(statusQuery.error, t)}
                </Alert>
              )}
              {notConfigured && <Alert severity="warning">{t('customModels.notConfigured')}</Alert>}
              {status?.transport === 'FTP' && (
                <Alert severity="warning">{t('add.plainFtpWarning')}</Alert>
              )}
              <TextField
                id="custom-model-source"
                label={t('add.sourceLabel')}
                required
                fullWidth
                error={Boolean(errors.source) || preview?.isValid === false}
                helperText={sourceHelper}
                slotProps={{ htmlInput: { dir: 'ltr' } }}
                {...fieldProps(form.register('source', { validate: validateWith(schemas.source) }))}
              />
              {previewQuery.isError && debouncedSource === source.trim() && (
                <Alert severity="error">
                  {t('add.previewFailed', { detail: errorMessage(previewQuery.error, t) })}
                </Alert>
              )}
              {preview?.filePath && (
                <Alert severity="info">{t('add.onlyFile', { path: preview.filePath })}</Alert>
              )}
              <TextField
                id="custom-model-destination"
                label={t('add.destinationLabel')}
                required
                fullWidth
                placeholder="Models/supertonic-3"
                error={Boolean(errors.destination)}
                helperText={destinationHelper}
                slotProps={{ htmlInput: { dir: 'ltr' } }}
                {...fieldProps(
                  form.register('destination', { validate: validateWith(schemas.destination) }),
                )}
              />
              {showName && (
                <TextField
                  id="custom-model-name"
                  label={t('add.nameLabel')}
                  required
                  fullWidth
                  error={Boolean(errors.name)}
                  helperText={
                    errors.name?.message ??
                    (preview?.derivedName && !preview.nameAvailable
                      ? t('add.nameExists', { name: preview.derivedName })
                      : t('add.nameNotDerived'))
                  }
                  {...fieldProps(form.register('name', { validate: validateWith(schemas.name) }))}
                />
              )}
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={close}>{t('shared.cancel')}</Button>
            <Button
              type="submit"
              variant="contained"
              disabled={notConfigured || !status || submitMutation.isPending}
            >
              {t('add.deploy')}
            </Button>
          </DialogActions>
        </Box>
      </Dialog>
      <Snackbar open={toast !== null} autoHideDuration={6000} onClose={() => setToast(null)}>
        <Alert severity="error" variant="filled" onClose={() => setToast(null)}>
          {toast}
        </Alert>
      </Snackbar>
    </>
  )
}
