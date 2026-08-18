import { Box, Chip, List, ListItemButton, ListItemText, Stack, Typography } from '@mui/material'
import { useExecutions } from '../hooks/usePromptExecution'

interface ExecutionHistoryProps {
  promptId: string
  onSelect: (executionId: string) => void
  selectedIds?: string[]
}

/** Past executions for a prompt, newest first (spec.md FR-042). Clicking an item opens its detail; `selectedIds` (comparison mode) highlights multi-selected rows. */
export function ExecutionHistory({ promptId, onSelect, selectedIds = [] }: ExecutionHistoryProps) {
  const { data } = useExecutions(promptId)

  if (!data || data.items.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        No executions yet — run the prompt to see its history here.
      </Typography>
    )
  }

  return (
    <List dense data-testid="execution-history-list">
      {data.items.map((execution) => (
        <ListItemButton
          key={execution.id}
          selected={selectedIds.includes(execution.id)}
          onClick={() => onSelect(execution.id)}
        >
          <ListItemText
            primary={
              <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                <Typography variant="body2">{execution.modelKey}</Typography>
                <Chip
                  size="small"
                  label={execution.outcome}
                  color={execution.outcome === 'Success' ? 'success' : 'error'}
                />
              </Stack>
            }
            secondary={
              <Box component="span">
                {execution.latencyMs !== null && `${execution.latencyMs}ms`}
                {execution.estimatedCostUsd !== null && ` · $${execution.estimatedCostUsd.toFixed(4)}`}
                {` · v${execution.versionNumber}`}
              </Box>
            }
          />
        </ListItemButton>
      ))}
    </List>
  )
}
