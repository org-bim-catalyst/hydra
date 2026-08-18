import { Alert, Box, Button, Chip, Divider, Grid, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
import { useAiModels, useAiProviders } from '../../chat/hooks/useAiCatalog'
import type { PromptDetail } from '../api/promptsApi'
import { ExecutionContextOptions } from './ExecutionContextOptions'
import { DEFAULT_EXECUTION_CONTEXT_OPTIONS, type ExecutionContextOptionsState } from './executionContextOptionsState'
import { ExecutionComparison } from './ExecutionComparison'
import { ExecutionHistory } from './ExecutionHistory'
import { isModelCompatible, unmetCapabilities } from './promptCapabilityUtils'
import { useExecutePromptStream, useSaveTestCase } from '../hooks/usePromptExecution'

interface PromptTestingConsoleProps {
  prompt: PromptDetail
}

/**
 * Split Testing Console (spec.md "Prompt Testing UI") — editor/variables/model settings on the
 * left, streamed output/token usage/cost/latency/provider/model on the right. Repeated execution
 * without leaving the workspace is just clicking Run again.
 */
export function PromptTestingConsole({ prompt }: PromptTestingConsoleProps) {
  const [variableValues, setVariableValues] = useState<Record<string, string>>({});
  const [providerId, setProviderId] = useState('')
  const [modelId, setModelId] = useState('')
  const [temperature, setTemperature] = useState<string>('')
  const [maxTokens, setMaxTokens] = useState<string>('')
  const [compareIds, setCompareIds] = useState<string[]>([])
  const [testCaseName, setTestCaseName] = useState('')
  const [savingTestCase, setSavingTestCase] = useState(false)
  const [contextOptions, setContextOptions] = useState<ExecutionContextOptionsState>(DEFAULT_EXECUTION_CONTEXT_OPTIONS)

  const { data: providers } = useAiProviders()
  const { data: models } = useAiModels(providerId || null)
  const selectedModel = models?.find((m) => m.id === modelId)

  const { output, isRunning, error, executionId, run } = useExecutePromptStream(prompt.id)
  const saveTestCase = useSaveTestCase(prompt.id)

  const missingRequired = useMemo(
    () => prompt.variables.filter((v) => v.isRequired && !variableValues[v.name]?.trim()),
    [prompt.variables, variableValues],
  )

  const handleRun = () => {
    if (missingRequired.length > 0 || !providerId || !modelId) {
      return
    }

    void run({
      variableValues,
      providerId,
      modelId,
      generationParameters: {
        temperature: temperature ? Number(temperature) : null,
        maxTokens: maxTokens ? Number(maxTokens) : null,
      },
      useRagContext: contextOptions.useRagContext,
      knowledgeBaseIds: contextOptions.useRagContext ? contextOptions.knowledgeBaseIds : null,
      useMemoryContext: contextOptions.useMemoryContext,
    })
  }

  const handleSaveTestCase = () => {
    if (!executionId) return
    saveTestCase.mutate(
      {
        name: testCaseName || `Test case ${new Date().toISOString()}`,
        variableValuesJson: JSON.stringify(variableValues),
        providerKey: providers?.find((p) => p.id === providerId)?.providerKey ?? '',
        modelKey: selectedModel?.modelKey ?? '',
        sourceExecutionId: executionId,
      },
      { onSuccess: () => setSavingTestCase(false) },
    )
  }

  return (
    <Grid container spacing={3}>
      <Grid size={{ xs: 12, md: 6 }}>
        <Stack spacing={2}>
          <Typography variant="subtitle1">Variables</Typography>
          {prompt.variables.map((variable) => (
            <TextField
              key={variable.name}
              label={variable.name}
              required={variable.isRequired}
              fullWidth
              multiline={variable.type === 'Text'}
              helperText={variable.description ?? undefined}
              value={variableValues[variable.name] ?? ''}
              onChange={(e) => setVariableValues((prev) => ({ ...prev, [variable.name]: e.target.value }))}
            />
          ))}

          <Divider />
          <Typography variant="subtitle1">Model settings</Typography>
          <TextField select label="Provider" value={providerId} onChange={(e) => { setProviderId(e.target.value); setModelId('') }}>
            {providers?.map((p) => (
              <MenuItem key={p.id} value={p.id}>
                {p.displayName}
              </MenuItem>
            ))}
          </TextField>
          <TextField select label="Model" value={modelId} onChange={(e) => setModelId(e.target.value)} disabled={!providerId}>
            {models?.map((m) => (
              <MenuItem key={m.id} value={m.id}>
                {m.displayName}
              </MenuItem>
            ))}
          </TextField>
          {selectedModel && !isModelCompatible(prompt.requiredCapabilities, selectedModel) && (
            <Alert severity="warning">
              This model does not support required capabilities: {unmetCapabilities(prompt.requiredCapabilities, selectedModel).join(', ')}.
            </Alert>
          )}
          <Stack direction="row" spacing={2}>
            <TextField label="Temperature" type="number" value={temperature} onChange={(e) => setTemperature(e.target.value)} />
            <TextField label="Max output tokens" type="number" value={maxTokens} onChange={(e) => setMaxTokens(e.target.value)} />
          </Stack>

          <Divider />
          <ExecutionContextOptions value={contextOptions} onChange={setContextOptions} />

          <Button
            variant="contained"
            onClick={handleRun}
            disabled={isRunning || !providerId || !modelId}
          >
            {isRunning ? 'Running…' : 'Run'}
          </Button>
          {missingRequired.length > 0 && (
            <Alert severity="error">
              {missingRequired.map((v) => v.name).join(', ')} {missingRequired.length === 1 ? 'is' : 'are'} required.
            </Alert>
          )}
        </Stack>
      </Grid>

      <Grid size={{ xs: 12, md: 6 }}>
        <Stack spacing={2}>
          <Typography variant="subtitle1">Output</Typography>
          <Paper variant="outlined" sx={{ p: 2, minHeight: 200 }}>
            <Typography data-testid="execution-output" variant="body2" sx={{ whiteSpace: 'pre-wrap' }}>
              {output}
            </Typography>
          </Paper>

          {error && <Alert severity="error">{error}</Alert>}

          {executionId && (
            <Stack spacing={1}>
              <Stack direction="row" spacing={1} data-testid="execution-token-usage">
                <Chip label={`Provider: ${providers?.find((p) => p.id === providerId)?.displayName ?? providerId}`} size="small" />
                <Chip label={`Model: ${selectedModel?.displayName ?? modelId}`} size="small" />
              </Stack>
              <Box data-testid="execution-cost">
                {savingTestCase ? (
                  <Stack direction="row" spacing={1}>
                    <TextField
                      size="small"
                      label="Test case name"
                      value={testCaseName}
                      onChange={(e) => setTestCaseName(e.target.value)}
                    />
                    <Button size="small" onClick={handleSaveTestCase}>
                      Save
                    </Button>
                  </Stack>
                ) : (
                  <Button size="small" onClick={() => setSavingTestCase(true)}>
                    Save as test case
                  </Button>
                )}
              </Box>
            </Stack>
          )}

          <Divider />
          <Typography variant="subtitle1">Execution history</Typography>
          <ExecutionHistory
            promptId={prompt.id}
            selectedIds={compareIds}
            onSelect={(id) =>
              setCompareIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id].slice(-3)))
            }
          />
          {compareIds.length >= 2 && (
            <>
              <Divider />
              <Typography variant="subtitle1">Comparison</Typography>
              <ExecutionComparison executionIds={compareIds} />
            </>
          )}
        </Stack>
      </Grid>
    </Grid>
  )
}
