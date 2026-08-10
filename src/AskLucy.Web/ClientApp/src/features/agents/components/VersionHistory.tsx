import { List, ListItemButton, ListItemText, Typography } from '@mui/material'
import { useAgentVersions } from '../hooks/useAgentVersions'

interface VersionHistoryProps {
  agentId: string
  selectedVersionNumber?: number | null
  onSelect?: (versionNumber: number) => void
}

/**
 * Every published version of an agent (spec.md User Story 6) — published versions are immutable,
 * so this is purely a read-only list; an execution started under an older version keeps reporting
 * that version even after newer ones exist (verified by `AgentVersioningTests`, not by this
 * component, which just displays whatever `ListAgentVersionsQuery` returns).
 */
export function VersionHistory({ agentId, selectedVersionNumber, onSelect }: VersionHistoryProps) {
  const { data: versions, isLoading } = useAgentVersions(agentId)

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
