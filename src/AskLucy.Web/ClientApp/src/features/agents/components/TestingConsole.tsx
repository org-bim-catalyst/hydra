import { Alert, Box, Button, Chip, CircularProgress, Paper, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { ExecutionTimeline } from './ExecutionTimeline'
import { VersionHistory } from './VersionHistory'
import { useAgentExecution, useStartAgentExecution } from '../hooks/useAgentExecution'
import { useAgentExecutionHub } from '../hooks/useAgentExecutionHub'

interface TestingConsoleProps {
  agentId: string
  publishedVersionNumber: number | null
}

const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled'])

/**
 * spec.md User Story 6 — a test environment: select any published version, run it, inspect the
 * result. Always sends `isTestExecution: true` (research.md Decision 12) — a mutating tool step is
 * skipped rather than executed, so no production data is ever modified from here, and every
 * result is visibly labeled a test execution so it's never confused with a live run.
 */
export function TestingConsole({ agentId, publishedVersionNumber }: TestingConsoleProps) {
  const [objective, setObjective] = useState('')
  const [selectedVersionNumber, setSelectedVersionNumber] = useState<number | null>(publishedVersionNumber)
  const [executionId, setExecutionId] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const startExecution = useStartAgentExecution()
  const { data: execution } = useAgentExecution(executionId)
  useAgentExecutionHub(executionId)

  const isRunning = execution !== undefined && !TERMINAL_STATUSES.has(execution.status)
  const canRun = objective && selectedVersionNumber !== null && !isRunning && !startExecution.isPending

  const handleRun = () => {
    setErrorMessage(null)
    startExecution.mutate(
      {
        agentId,
        agentVersionNumber: selectedVersionNumber,
        objective,
        conversationIntegrationMode: 'Standalone',
        userChatId: null,
        isTestExecution: true,
      },
      {
        onSuccess: (summary) => setExecutionId(summary.id),
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not start the test execution. Please try again.'),
      },
    )
  }

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="subtitle1" sx={{ mb: 2 }}>
        Testing Console
      </Typography>

      <Stack direction={{ xs: 'column', sm: 'row' }} spacing={3}>
        <Box sx={{ minWidth: 220 }}>
          <Typography variant="caption" color="text.secondary">
            Version
          </Typography>
          <VersionHistory agentId={agentId} selectedVersionNumber={selectedVersionNumber} onSelect={setSelectedVersionNumber} />
        </Box>

        <Stack spacing={2} sx={{ flex: 1 }}>
          <TextField
            label="Objective"
            required
            multiline
            minRows={2}
            value={objective}
            onChange={(e) => setObjective(e.target.value)}
            disabled={isRunning}
          />

          <Box>
            <Button variant="contained" onClick={handleRun} disabled={!canRun}>
              {isRunning ? 'Running…' : 'Run Test'}
            </Button>
          </Box>

          {errorMessage && <Alert severity="error">{errorMessage}</Alert>}

          {execution && (
            <Box>
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
                <Chip label="Test execution" size="small" color="secondary" variant="outlined" />
                <Chip
                  label={execution.status}
                  color={execution.status === 'Completed' ? 'success' : execution.status === 'Failed' ? 'error' : 'default'}
                  size="small"
                />
                {isRunning && <CircularProgress size={16} />}
              </Stack>

              {execution.status === 'Completed' && execution.finalOutputText && (
                <Typography data-testid="test-execution-result" variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                  {execution.finalOutputText}
                </Typography>
              )}

              {execution.status === 'Failed' && execution.terminationReason && <Alert severity="error">{execution.terminationReason}</Alert>}

              <Box sx={{ mt: 2 }}>
                <ExecutionTimeline steps={execution.steps} />
              </Box>
            </Box>
          )}
        </Stack>
      </Stack>
    </Paper>
  )
}
