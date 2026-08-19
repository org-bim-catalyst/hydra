import { RiArrowLeftLine, RiSearchLine } from '@remixicon/react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  List,
  ListItemButton,
  ListItemText,
  Stack,
  TextField,
  Typography,
} from '@mui/material'
import { useMemo, useState } from 'react'
import { unmetCapabilities } from '../../prompts/components/promptCapabilityUtils'
import * as promptConversationApi from '../../prompts/api/promptConversationApi'
import * as promptsApi from '../../prompts/api/promptsApi'
import type { PromptDetail } from '../../prompts/api/promptsApi'
import { useAiModels } from '../hooks/useAiCatalog'
import { useQuery } from '@tanstack/react-query'

interface InsertPromptPickerProps {
  open: boolean
  onClose: () => void
  chatId: string
  providerId: string | null
  modelId: string | null
  onInserted: () => void
}

/**
 * "Insert Prompt" picker for the chat composer (spec.md FR-080, User Story 5). Two steps: search
 * and select a saved prompt, then resolve/enter its variable values — a capability-incompatible
 * conversation model blocks submission with a specific warning before anything is sent (US5 AC3),
 * and a missing required variable blocks it locally the same way the Testing Console already does
 * (US5 AC1; the server re-validates regardless — this is a fail-fast UX affordance, not the only
 * enforcement).
 */
export function InsertPromptPicker({
  open,
  onClose,
  chatId,
  providerId,
  modelId,
  onInserted,
}: InsertPromptPickerProps) {
  const [query, setQuery] = useState('')
  const [selectedPrompt, setSelectedPrompt] = useState<PromptDetail | null>(null)
  const [variableValues, setVariableValues] = useState<Record<string, string>>({})
  const [isSending, setIsSending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const { data: searchResults } = useQuery({
    queryKey: ['prompts', 'list', 'insert-picker', query],
    queryFn: () =>
      promptsApi.listPrompts({ view: 'All', q: query.trim() || undefined, pageSize: 20 }),
    enabled: open && selectedPrompt === null,
  })

  const { data: models } = useAiModels(providerId)
  const selectedModel = models?.find((m) => m.id === modelId)
  const capabilityWarnings =
    selectedPrompt && selectedModel
      ? unmetCapabilities(selectedPrompt.requiredCapabilities, selectedModel)
      : []

  const missingRequired = useMemo(
    () =>
      (selectedPrompt?.variables ?? []).filter(
        (v) => v.isRequired && !variableValues[v.name]?.trim(),
      ),
    [selectedPrompt, variableValues],
  )

  const reset = () => {
    setQuery('')
    setSelectedPrompt(null)
    setVariableValues({})
    setError(null)
  }

  const handleClose = () => {
    if (isSending) return
    reset()
    onClose()
  }

  const handleSelectPrompt = async (id: string) => {
    const prompt = await promptsApi.getPrompt(id)
    setSelectedPrompt(prompt)
    setVariableValues({})
  }

  const handleInsert = async () => {
    if (!selectedPrompt || missingRequired.length > 0 || capabilityWarnings.length > 0) return
    setIsSending(true)
    setError(null)
    try {
      // eslint-disable-next-line @typescript-eslint/no-unused-vars
      for await (const _delta of promptConversationApi.insertPromptIntoConversation(
        chatId,
        selectedPrompt.id,
        variableValues,
      )) {
        // Content deltas are relayed to persisted chat history server-side; onInserted() below
        // triggers a refetch of that history rather than accumulating deltas locally here.
      }
      onInserted()
      reset()
      onClose()
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'Inserting the prompt failed. Please try again.',
      )
    } finally {
      setIsSending(false)
    }
  }

  return (
    <Dialog open={open} onClose={handleClose} fullWidth maxWidth="sm">
      {selectedPrompt === null ? (
        <>
          <DialogTitle>Insert a saved prompt</DialogTitle>
          <DialogContent>
            <TextField
              autoFocus
              fullWidth
              size="small"
              placeholder="Search prompts…"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <RiSearchLine size={20} />
                    </InputAdornment>
                  ),
                },
              }}
              sx={{ mb: 2 }}
            />
            <List data-testid="insert-prompt-search-results">
              {(searchResults?.items ?? []).map((prompt) => (
                <ListItemButton key={prompt.id} onClick={() => void handleSelectPrompt(prompt.id)}>
                  <ListItemText primary={prompt.name} secondary={prompt.description} />
                </ListItemButton>
              ))}
              {searchResults?.items.length === 0 && (
                <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>
                  No prompts found.
                </Typography>
              )}
            </List>
          </DialogContent>
          <DialogActions>
            <Button onClick={handleClose}>Cancel</Button>
          </DialogActions>
        </>
      ) : (
        <>
          <DialogTitle>
            <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
              <IconButton
                size="small"
                aria-label="Back to search"
                onClick={() => setSelectedPrompt(null)}
              >
                <RiArrowLeftLine size={20} />
              </IconButton>
              {selectedPrompt.name}
            </Stack>
          </DialogTitle>
          <DialogContent>
            <Stack spacing={2}>
              {selectedPrompt.variables.map((variable) => (
                <TextField
                  key={variable.name}
                  label={variable.name}
                  required={variable.isRequired}
                  fullWidth
                  multiline={variable.type === 'Text'}
                  helperText={variable.description ?? undefined}
                  value={variableValues[variable.name] ?? ''}
                  onChange={(e) =>
                    setVariableValues((prev) => ({ ...prev, [variable.name]: e.target.value }))
                  }
                />
              ))}

              {capabilityWarnings.length > 0 && (
                <Alert severity="warning">
                  This conversation's model does not support required capabilities:{' '}
                  {capabilityWarnings.join(', ')}. Change the conversation's model before inserting
                  this prompt.
                </Alert>
              )}
              {missingRequired.length > 0 && (
                <Alert severity="error">
                  {missingRequired.map((v) => v.name).join(', ')}{' '}
                  {missingRequired.length === 1 ? 'is' : 'are'} required.
                </Alert>
              )}
              {error && <Alert severity="error">{error}</Alert>}
            </Stack>
          </DialogContent>
          <DialogActions>
            <Button onClick={handleClose} disabled={isSending}>
              Cancel
            </Button>
            <Button
              variant="contained"
              onClick={() => void handleInsert()}
              disabled={isSending || missingRequired.length > 0 || capabilityWarnings.length > 0}
            >
              {isSending ? 'Inserting…' : 'Insert'}
            </Button>
          </DialogActions>
        </>
      )}
    </Dialog>
  )
}
