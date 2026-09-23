import { useEffect, useState } from 'react'
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
import * as customModelsApi from '../../api/adminCustomModelsApi'
import type { CustomModelSummary } from '../../api/adminCustomModelsApi'
import { errorMessage } from './errorMessage'

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
// ForgotPasswordPage. The server re-validates everything; these only catch the obvious locally.
const sourceSchema = z.string().trim().min(1, 'Enter a Hugging Face repository URL.').max(2048, 'That URL is too long.')
const destinationSchema = z.string().trim().min(1, 'Enter a destination folder.').max(512, 'That path is too long.')
const nameSchema = z
  .string()
  .trim()
  .min(1, 'Enter a name for this model.')
  .regex(/^[\p{L}\p{N}][\p{L}\p{N} ._-]{0,99}$/u, 'Use up to 100 letters, digits, spaces, dots, dashes or underscores.')

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
    nameForced || (preview?.isValid === true && (preview.derivedName === null || !preview.nameAvailable))

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
      if (!placed) setToast(errorMessage(err))
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
    (preview?.repositoryId ? `${preview.repositoryId} @ ${preview.revision ?? 'main'}` : 'e.g. https://huggingface.co/Supertone/supertonic-3')
  const prefixes = status?.allowedDestinationPrefixes ?? []
  const destinationHelper =
    errors.destination?.message ??
    (prefixes.length > 0
      ? `Relative to the deployment root. Must start with ${prefixes.map((p) => `${p}/`).join(' or ')}`
      : 'Relative to the deployment root.')

  return (
    <>
      <Dialog open={open} onClose={close} maxWidth="sm" fullWidth>
        <Box component="form" onSubmit={onSubmit} noValidate>
          <DialogTitle>Add custom model</DialogTitle>
          <DialogContent>
            <DialogContentText sx={{ mb: 2 }}>
              The server downloads the whole repository from Hugging Face and uploads it to the destination folder.
              You can close this page while it runs.
            </DialogContentText>
            <Stack spacing={2}>
              {statusQuery.isError && (
                <Alert
                  severity="error"
                  action={
                    <Button color="inherit" size="small" onClick={() => void statusQuery.refetch()}>
                      Retry
                    </Button>
                  }
                >
                  {errorMessage(statusQuery.error)}
                </Alert>
              )}
              {notConfigured && (
                <Alert severity="warning">
                  Deployment not configured. Ask whoever runs this server to set up the deployment target first.
                </Alert>
              )}
              {status?.transport === 'FTP' && (
                <Alert severity="warning">
                  This server deploys over plain FTP, so files and credentials travel unencrypted.
                </Alert>
              )}
              <TextField
                id="custom-model-source"
                label="Source"
                required
                fullWidth
                error={Boolean(errors.source) || preview?.isValid === false}
                helperText={sourceHelper}
                {...fieldProps(form.register('source', { validate: validateWith(sourceSchema) }))}
              />
              {previewQuery.isError && debouncedSource === source.trim() && (
                <Alert severity="error">We couldn&apos;t check that URL. {errorMessage(previewQuery.error)}</Alert>
              )}
              {preview?.ignoredFilePath && (
                <Alert severity="info">
                  Only whole repositories are deployed; {preview.ignoredFilePath} is ignored.
                </Alert>
              )}
              <TextField
                id="custom-model-destination"
                label="Destination"
                required
                fullWidth
                placeholder="Models/supertonic-3"
                error={Boolean(errors.destination)}
                helperText={destinationHelper}
                {...fieldProps(form.register('destination', { validate: validateWith(destinationSchema) }))}
              />
              {showName && (
                <TextField
                  id="custom-model-name"
                  label="Name"
                  required
                  fullWidth
                  error={Boolean(errors.name)}
                  helperText={
                    errors.name?.message ??
                    (preview?.derivedName && !preview.nameAvailable
                      ? `A model called ${preview.derivedName} already exists. Choose another name.`
                      : 'The name couldn’t be taken from the URL.')
                  }
                  {...fieldProps(form.register('name', { validate: validateWith(nameSchema) }))}
                />
              )}
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={close}>Cancel</Button>
            <Button
              type="submit"
              variant="contained"
              disabled={notConfigured || !status || submitMutation.isPending}
            >
              Deploy
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
