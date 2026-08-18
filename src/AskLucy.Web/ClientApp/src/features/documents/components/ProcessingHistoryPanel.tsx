import { List, ListItem, ListItemText } from '@mui/material'
import { useProcessingHistory } from '../hooks/useDocuments'
import { EmptyState } from '../../../components/EmptyState'
import { ErrorState } from '../../../components/ErrorState'

function formatEventType(eventType: string): string {
  return eventType.replace(/([a-z])([A-Z])/g, '$1 $2')
}

interface ProcessingHistoryPanelProps {
  documentId: string
}

/** FR-013, US2 AC5 — the append-only, newest-first processing history. */
export function ProcessingHistoryPanel({ documentId }: ProcessingHistoryPanelProps) {
  const { data, isLoading, isError, refetch } = useProcessingHistory(documentId)

  if (isError) {
    return <ErrorState title="Could not load processing history" onRetry={() => void refetch()} />
  }

  if (!isLoading && (data?.length ?? 0) === 0) {
    return <EmptyState title="No processing history yet" />
  }

  return (
    <List dense>
      {data?.map((log) => (
        <ListItem key={log.id} disableGutters>
          <ListItemText
            primary={formatEventType(log.eventType)}
            secondary={`${new Date(log.occurredAtUtc).toLocaleString()}${log.detail ? ` — ${log.detail}` : ''}`}
          />
        </ListItem>
      ))}
    </List>
  )
}
