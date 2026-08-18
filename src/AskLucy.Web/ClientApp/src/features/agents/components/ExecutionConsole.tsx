import { Alert, Box, Button, Chip, CircularProgress, MenuItem, Paper, Stack, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import type { AgentConversationIntegrationMode } from '../api/agentExecutionsApi'
import { ApprovalDialog } from './ApprovalDialog'
import { ExecutionTimeline } from './ExecutionTimeline'
import {
  useAgentExecution,
  useCancelAgentExecution,
  usePauseAgentExecution,
  useResumeAgentExecution,
  useStartAgentExecution,
} from '../hooks/useAgentExecution'
import { useAgentExecutionHub } from '../hooks/useAgentExecutionHub'

interface ExecutionConsoleProps {
  agentId: string
}

const TERMINAL_STATUSES = new Set(['Completed', 'Failed', 'Cancelled'])

const CONVERSATION_MODES: { value: AgentConversationIntegrationMode; label: string }[] = [
  { value: 'Standalone', label: 'Standalone (no conversation)' },
  { value: 'NewConversation', label: 'Start a new conversation' },
  { value: 'ExistingConversation', label: 'Use an existing conversation' },
]

/**
 * Execution trigger, live step/tool-activity timeline, and pause/resume/cancel controls (spec.md
 * User Story 1 + User Story 4), including all three conversation-integration modes (FR-051/FR-052).
 * `useAgentExecution` polls every 2s while non-terminal; `useAgentExecutionHub` pushes over
 * `AgentExecutionHub` to shorten that latency, invalidating the same query cache — the REST poll
 * remains the source of truth and reconciliation fallback if a live push is missed.
 */
export function ExecutionConsole({ agentId }: ExecutionConsoleProps) {
  const [objective, setObjective] = useState('')
  const [conversationMode, setConversationMode] = useState<AgentConversationIntegrationMode>('Standalone')
  const [userChatId, setUserChatId] = useState('')
  const [executionId, setExecutionId] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const startExecution = useStartAgentExecution()
  const { data: execution } = useAgentExecution(executionId)
  useAgentExecutionHub(executionId)
  const pauseExecution = usePauseAgentExecution(executionId ?? '')
  const resumeExecution = useResumeAgentExecution(executionId ?? '')
  const cancelExecution = useCancelAgentExecution(executionId ?? '')

  const handleRun = () => {
    setErrorMessage(null)
    startExecution.mutate(
      {
        agentId,
        agentVersionNumber: null,
        objective,
        conversationIntegrationMode: conversationMode,
        userChatId: conversationMode === 'ExistingConversation' ? userChatId || null : null,
      },
      {
        onSuccess: (summary) => setExecutionId(summary.id),
        onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not start the execution. Please try again.'),
      },
    )
  }

  const isRunning = execution !== undefined && !TERMINAL_STATUSES.has(execution.status)
  const canRun = objective && !isRunning && !startExecution.isPending && (conversationMode !== 'ExistingConversation' || userChatId)

  return (
    <Paper sx={{ p: 3 }}>
      <Typography variant="subtitle1" sx={{ mb: 2 }}>
        Run this agent
      </Typography>

      <Stack spacing={2}>
        <TextField
          label="Objective"
          required
          multiline
          minRows={2}
          value={objective}
          onChange={(e) => setObjective(e.target.value)}
          disabled={isRunning}
        />

        <TextField
          label="Conversation"
          select
          value={conversationMode}
          onChange={(e) => setConversationMode(e.target.value as AgentConversationIntegrationMode)}
          disabled={isRunning}
        >
          {CONVERSATION_MODES.map((mode) => (
            <MenuItem key={mode.value} value={mode.value}>
              {mode.label}
            </MenuItem>
          ))}
        </TextField>

        {conversationMode === 'ExistingConversation' && (
          <TextField
            label="Conversation ID"
            required
            value={userChatId}
            onChange={(e) => setUserChatId(e.target.value)}
            disabled={isRunning}
          />
        )}

        <Box>
          <Button variant="contained" onClick={handleRun} disabled={!canRun}>
            {isRunning ? 'Running…' : 'Run'}
          </Button>
        </Box>

        {errorMessage && <Alert severity="error">{errorMessage}</Alert>}

        {execution && (
          <Box>
            <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
              <Chip
                label={execution.status}
                color={execution.status === 'Completed' ? 'success' : execution.status === 'Failed' ? 'error' : 'default'}
                size="small"
              />
              {isRunning && <CircularProgress size={16} />}

              {execution.status === 'Running' && (
                <Button size="small" onClick={() => pauseExecution.mutate()} disabled={pauseExecution.isPending}>
                  Pause
                </Button>
              )}
              {execution.status === 'Paused' && (
                <Button size="small" onClick={() => resumeExecution.mutate()} disabled={resumeExecution.isPending}>
                  Resume
                </Button>
              )}
              {!TERMINAL_STATUSES.has(execution.status) && (
                <Button size="small" color="error" onClick={() => cancelExecution.mutate()} disabled={cancelExecution.isPending}>
                  Cancel
                </Button>
              )}
            </Stack>

            {execution.status === 'Completed' && execution.finalOutputText && (
              <Typography data-testid="execution-result" variant="body1" sx={{ whiteSpace: 'pre-wrap' }}>
                {execution.finalOutputText}
              </Typography>
            )}

            {execution.status === 'Failed' && execution.terminationReason && (
              <Alert severity="error">{execution.terminationReason}</Alert>
            )}

            {execution.status === 'Cancelled' && execution.terminationReason && (
              <Alert severity="warning">{execution.terminationReason}</Alert>
            )}

            <Box sx={{ mt: 2 }}>
              <ExecutionTimeline steps={execution.steps} />
            </Box>
          </Box>
        )}
      </Stack>

      {execution?.status === 'WaitingForApproval' &&
        (() => {
          const pendingApproval = execution.approvals.find((a) => a.decision === 'Pending')
          return pendingApproval ? <ApprovalDialog executionId={execution.id} approval={pendingApproval} /> : null
        })()}
    </Paper>
  )
}
