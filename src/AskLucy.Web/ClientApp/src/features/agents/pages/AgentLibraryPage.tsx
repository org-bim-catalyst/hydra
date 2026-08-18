import AddIcon from '@mui/icons-material/Add'
import { Box, Button, Card, CardActionArea, CardActions, CardContent, Chip, Stack, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import { EmptyState } from '../../../components/EmptyState'
import { useSearchAgents } from '../hooks/useAgents'
import { useArchiveAgent, useDeleteAgent, useDuplicateAgent, useRestoreAgent } from '../hooks/useAgentMutations'

/** Agent Library — a user's own agents (spec.md User Story 1/User Story 6). */
export function AgentLibraryPage() {
  const navigate = useNavigate()
  const { data, isLoading } = useSearchAgents({ pageSize: 50 })
  const agents = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])
  const duplicateAgent = useDuplicateAgent()
  const archiveAgent = useArchiveAgent()
  const restoreAgent = useRestoreAgent()
  const deleteAgent = useDeleteAgent()
  const [pendingDeleteId, setPendingDeleteId] = useState<string | null>(null)

  return (
    <AppShell
      title="Agents"
      actions={
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => navigate('/agents/new')}>
          New Agent
        </Button>
      }
    >
      <Box sx={{ p: 3 }}>
        {!isLoading && agents.length === 0 && (
          <EmptyState title="No agents yet" description="Create your first agent to get started." />
        )}

        <Stack spacing={2}>
          {agents.map((agent) => (
            <Card key={agent.id} variant="outlined">
              <CardActionArea onClick={() => navigate(`/agents/${agent.id}`)}>
                <CardContent>
                  <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 0.5 }}>
                    <Typography variant="subtitle1">{agent.name}</Typography>
                    <Chip label={agent.status} size="small" />
                    <Chip label={agent.agentType} size="small" variant="outlined" />
                  </Stack>
                  {agent.description && (
                    <Typography variant="body2" color="text.secondary">
                      {agent.description}
                    </Typography>
                  )}
                </CardContent>
              </CardActionArea>
              <CardActions>
                <Button size="small" onClick={() => duplicateAgent.mutate(agent.id)} disabled={duplicateAgent.isPending}>
                  Duplicate
                </Button>
                {agent.status === 'Archived' ? (
                  <Button size="small" onClick={() => restoreAgent.mutate(agent.id)} disabled={restoreAgent.isPending}>
                    Restore
                  </Button>
                ) : (
                  <Button size="small" onClick={() => archiveAgent.mutate(agent.id)} disabled={archiveAgent.isPending}>
                    Archive
                  </Button>
                )}
                <Button size="small" color="error" onClick={() => setPendingDeleteId(agent.id)}>
                  Delete
                </Button>
              </CardActions>
            </Card>
          ))}
        </Stack>
      </Box>

      <ConfirmDialog
        open={pendingDeleteId !== null}
        title="Delete this agent?"
        description="This removes the agent from your library. Its published versions and execution history are kept for audit purposes."
        confirmLabel="Delete"
        onConfirm={() => {
          if (pendingDeleteId) deleteAgent.mutate(pendingDeleteId)
          setPendingDeleteId(null)
        }}
        onCancel={() => setPendingDeleteId(null)}
      />
    </AppShell>
  )
}
