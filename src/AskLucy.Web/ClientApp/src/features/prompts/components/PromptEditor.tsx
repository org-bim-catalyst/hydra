import { Alert, Box, Button, MenuItem, Paper, Snackbar, Stack, TextField, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
import { useForm } from 'react-hook-form'
import { useNavigate } from 'react-router'
import type { PromptDetail, PromptType, PromptVariable, SavePromptInput } from '../api/promptsApi'
import { NO_REQUIRED_CAPABILITIES } from '../api/promptsApi'
import { useCreatePrompt, useUpdatePrompt } from '../hooks/usePromptMutations'
import { syncVariablesWithPlaceholders } from './promptVariableSync'
import { VariableEditor } from './VariableEditor'

const PROMPT_TYPES: PromptType[] = [
  'Chat',
  'System',
  'Instruction',
  'Summarization',
  'Translation',
  'Extraction',
  'Classification',
  'Rag',
  'StructuredOutput',
]

const PLACEHOLDER_PATTERN = /\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}/g

function detectPlaceholders(...fields: (string | undefined)[]): string[] {
  const found = new Set<string>()
  for (const field of fields) {
    if (!field) continue
    for (const match of field.matchAll(PLACEHOLDER_PATTERN)) {
      found.add(match[1])
    }
  }
  return [...found]
}

interface PromptFormValues {
  name: string
  description: string
  promptType: PromptType
  systemInstructions: string
  developerInstructions: string
  userInstructions: string
  contextText: string
  examplesText: string
  outputInstructions: string
  constraints: string
}

interface PromptEditorProps {
  /** Present when editing an existing prompt; absent when creating a new one. */
  prompt?: PromptDetail
}

/**
 * Prompt content editor (spec.md FR-002, FR-005, "Prompt Editor" UI requirements) — every
 * structural component (system/developer/user instructions, context, examples, output
 * instructions, constraints) is a distinct field, never collapsed into one text blob.
 * Variables are re-detected from the content fields on every keystroke and synced into the
 * `VariableEditor` below so a user always sees exactly the placeholders their content
 * currently references (FR-010, FR-014).
 */
export function PromptEditor({ prompt }: PromptEditorProps) {
  const navigate = useNavigate()
  const isEditing = prompt !== undefined
  const createPrompt = useCreatePrompt()
  const updatePrompt = useUpdatePrompt()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [variables, setVariables] = useState<PromptVariable[]>(prompt?.variables ?? [])

  const { register, handleSubmit, watch } = useForm<PromptFormValues>({
    values: {
      name: prompt?.name ?? '',
      description: prompt?.description ?? '',
      promptType: prompt?.promptType ?? 'Chat',
      systemInstructions: prompt?.systemInstructions ?? '',
      developerInstructions: prompt?.developerInstructions ?? '',
      userInstructions: prompt?.userInstructions ?? '',
      contextText: prompt?.contextText ?? '',
      examplesText: prompt?.examplesText ?? '',
      outputInstructions: prompt?.outputInstructions ?? '',
      constraints: prompt?.constraints ?? '',
    },
  })

  const watchedFields = watch([
    'systemInstructions',
    'developerInstructions',
    'userInstructions',
    'contextText',
    'examplesText',
    'outputInstructions',
    'constraints',
  ])

  const detectedNames = useMemo(() => detectPlaceholders(...watchedFields), [watchedFields])
  const syncedVariables = useMemo(() => syncVariablesWithPlaceholders(variables, detectedNames), [variables, detectedNames])

  const characterCount = watchedFields.reduce((sum, field) => sum + (field?.length ?? 0), 0)
  const estimatedTokens = Math.ceil(characterCount / 4)

  const submitting = createPrompt.isPending || updatePrompt.isPending

  const onSubmit = handleSubmit((values) => {
    const input: SavePromptInput = {
      name: values.name,
      description: values.description || null,
      promptType: values.promptType,
      systemInstructions: values.systemInstructions || null,
      developerInstructions: values.developerInstructions || null,
      userInstructions: values.userInstructions,
      contextText: values.contextText || null,
      examplesText: values.examplesText || null,
      outputInstructions: values.outputInstructions || null,
      constraints: values.constraints || null,
      requiredCapabilities: prompt?.requiredCapabilities ?? NO_REQUIRED_CAPABILITIES,
      variables: syncedVariables,
    }

    const onError = (err: unknown) =>
      setErrorMessage(err instanceof Error ? err.message : 'Could not save the prompt. Please try again.')

    if (isEditing) {
      updatePrompt.mutate(
        { id: prompt.id, input },
        { onSuccess: () => navigate(`/prompts/${prompt.id}`), onError },
      )
    } else {
      createPrompt.mutate(input, { onSuccess: (created) => navigate(`/prompts/${created.id}`), onError })
    }
  })

  return (
    <Box component="form" onSubmit={onSubmit} sx={{ maxWidth: 900, mx: 'auto', p: 3 }}>
      <Stack spacing={3}>
        <Typography variant="h5">{isEditing ? 'Edit prompt' : 'New Prompt'}</Typography>

        <Stack direction="row" spacing={2}>
          <TextField label="Name" fullWidth required {...register('name', { required: true })} />
          <TextField select label="Type" sx={{ minWidth: 200 }} {...register('promptType')}>
            {PROMPT_TYPES.map((type) => (
              <MenuItem key={type} value={type}>
                {type}
              </MenuItem>
            ))}
          </TextField>
        </Stack>

        <TextField label="Description" fullWidth multiline minRows={2} {...register('description')} />

        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack spacing={2}>
            <Typography variant="subtitle1">Structure</Typography>
            <TextField label="System instructions" fullWidth multiline minRows={2} {...register('systemInstructions')} />
            <TextField label="Developer instructions" fullWidth multiline minRows={2} {...register('developerInstructions')} />
            <TextField
              label="User instructions"
              fullWidth
              required
              multiline
              minRows={4}
              helperText="Reference variables with {{name}} — they're auto-detected below."
              {...register('userInstructions', { required: true })}
            />
            <TextField label="Context" fullWidth multiline minRows={2} {...register('contextText')} />
            <TextField label="Examples" fullWidth multiline minRows={2} {...register('examplesText')} />
            <TextField label="Output instructions" fullWidth multiline minRows={2} {...register('outputInstructions')} />
            <TextField label="Constraints" fullWidth multiline minRows={2} {...register('constraints')} />
            <Typography variant="caption" color="text.secondary">
              {characterCount.toLocaleString()} characters · ~{estimatedTokens.toLocaleString()} tokens (estimated)
            </Typography>
          </Stack>
        </Paper>

        <Paper variant="outlined" sx={{ p: 2 }}>
          <Stack spacing={2}>
            <Typography variant="subtitle1">Variables</Typography>
            <VariableEditor variables={syncedVariables} onChange={setVariables} />
          </Stack>
        </Paper>

        <Stack direction="row" spacing={2} sx={{ justifyContent: 'flex-end' }}>
          <Button variant="outlined" onClick={() => navigate(-1)}>
            Cancel
          </Button>
          <Button type="submit" variant="contained" disabled={submitting}>
            Save
          </Button>
        </Stack>
      </Stack>

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={5000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </Box>
  )
}
