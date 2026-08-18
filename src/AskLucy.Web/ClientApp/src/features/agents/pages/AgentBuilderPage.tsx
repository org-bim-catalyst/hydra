import { Alert, Box, Button, Paper, Snackbar, Stack, Typography } from '@mui/material'
import { useState } from 'react'
import { useParams } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { AgentBuilder } from '../components/AgentBuilder'
import { ExecutionConsole } from '../components/ExecutionConsole'
import { ExecutionHistoryList } from '../components/ExecutionHistoryList'
import { TestingConsole } from '../components/TestingConsole'
import { useAgent } from '../hooks/useAgents'
import { usePublishAgentVersion } from '../hooks/useAgentMutations'

/** Create (`/agents/new`) or edit/run (`/agents/:id`) an agent (spec.md User Story 1). */
export function AgentBuilderPage() {
  const { id } = useParams<{ id: string }>()
  const isCreating = id === undefined || id === 'new'
  const { data: agent, isLoading } = useAgent(isCreating ? null : (id ?? null))
  const publishVersion = usePublishAgentVersion()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  if (!isCreating && isLoading) {
    return (
      <AppShell title="Loading…">
        <div />
      </AppShell>
    )
  }

  return (
    <AppShell title={isCreating ? 'New Agent' : (agent?.name ?? 'Agent')}>
      <AgentBuilder agent={agent} />

      {!isCreating && agent && (
        <Box sx={{ maxWidth: 900, mx: 'auto', px: 3, pb: 3 }}>
          <Stack spacing={2}>
            {/* spec.md FR-007-FR-010 — publishes an immutable snapshot of the current draft. */}
            <Box>
              <Button
                variant="outlined"
                disabled={publishVersion.isPending}
                onClick={() =>
                  publishVersion.mutate(
                    { id: agent.id, changeDescription: null },
                    { onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Could not publish this agent.') },
                  )
                }
              >
                Publish
              </Button>
            </Box>

            <ExecutionConsole agentId={agent.id} />

            <TestingConsole agentId={agent.id} publishedVersionNumber={agent.publishedVersionNumber} />

            <Paper sx={{ p: 3 }}>
              <Typography variant="subtitle1" sx={{ mb: 2 }}>
                Execution History
              </Typography>
              <ExecutionHistoryList agentId={agent.id} />
            </Paper>
          </Stack>
        </Box>
      )}

      <Snackbar open={errorMessage !== null} autoHideDuration={6000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
