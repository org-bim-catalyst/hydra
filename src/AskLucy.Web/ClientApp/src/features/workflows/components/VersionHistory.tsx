import { List, ListItemButton, ListItemText, Typography } from '@mui/material'
import { useWorkflowVersions } from '../hooks/useWorkflowVersions'

interface VersionHistoryProps {
  workflowId: string
  selectedVersionNumber?: number | null
  onSelect?: (versionNumber: number) => void
}

/**
 * Every published version of a workflow (spec.md User Story 3) — published versions are
 * immutable, so this is purely a read-only list; an execution started under an older version
 * keeps reporting that version even after newer ones exist (verified by `WorkflowVersioningTests`,
 * not by this component, which just displays whatever `ListWorkflowVersionsQuery` returns).
 */
export function VersionHistory({ workflowId, selectedVersionNumber, onSelect }: VersionHistoryProps) {
  const { data: versions, isLoading } = useWorkflowVersions(workflowId)

  if (isLoading) {
    return (
      <Typography variant="body2" color="text.secondary">
        Loading versions…
      </Typography>
    )
  }

  if (!versions || versions.length === 0) {
    return (
      <Typography variant="body2" color="text.secondary">
        Not published yet.
      </Typography>
    )
  }

  return (
    <List dense data-testid="version-history-list">
      {versions.map((version) => (
        <ListItemButton
          key={version.id}
          selected={version.versionNumber === selectedVersionNumber}
          onClick={() => onSelect?.(version.versionNumber)}
          data-testid="version-history-row"
        >
          <ListItemText
            primary={`v${version.versionNumber}`}
            secondary={`${new Date(version.createdAtUtc).toLocaleString()}${version.changeDescription ? ` — ${version.changeDescription}` : ''}`}
          />
        </ListItemButton>
      ))}
    </List>
  )
}
