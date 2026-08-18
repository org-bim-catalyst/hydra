import { Button, Checkbox, List, ListItem, ListItemText, Stack, Typography } from '@mui/material'
import { useState } from 'react'
import { useDuplicateVersion, useRestoreVersion, useVersions } from '../hooks/usePromptVersions'
import { VersionComparison } from './VersionComparison'

interface VersionHistoryProps {
  promptId: string
}

/** Lists every version (spec.md FR-032), newest first — restore/duplicate any of them, or select two to compare. */
export function VersionHistory({ promptId }: VersionHistoryProps) {
  const { data: versions } = useVersions(promptId)
  const restoreVersion = useRestoreVersion(promptId)
  const duplicateVersion = useDuplicateVersion(promptId)
  const [selected, setSelected] = useState<number[]>([])

  const toggleSelect = (versionNumber: number) => {
    setSelected((prev) =>
      prev.includes(versionNumber) ? prev.filter((v) => v !== versionNumber) : [...prev, versionNumber].slice(-2),
    )
  }

  if (!versions || versions.length === 0) {
    return null
  }

  return (
    <Stack spacing={2}>
      <List data-testid="version-history-list">
        {[...versions]
          .sort((a, b) => b.versionNumber - a.versionNumber)
          .map((version) => (
            <ListItem
              key={version.id}
              secondaryAction={
                <Stack direction="row" spacing={1}>
                  <Button size="small" onClick={() => restoreVersion.mutate(version.versionNumber)}>
                    Restore
                  </Button>
                  <Button size="small" onClick={() => duplicateVersion.mutate(version.versionNumber)}>
                    Duplicate
                  </Button>
                </Stack>
              }
            >
              <Checkbox
                checked={selected.includes(version.versionNumber)}
                onChange={() => toggleSelect(version.versionNumber)}
                slotProps={{ input: { 'aria-label': `Select version ${version.versionNumber} for comparison` } }}
              />
              <ListItemText
                primary={`v${version.versionNumber}${version.changeDescription ? ` — ${version.changeDescription}` : ''}`}
                secondary={new Date(version.createdAtUtc).toLocaleString()}
              />
            </ListItem>
          ))}
      </List>

      {selected.length === 2 ? (
        <VersionComparison promptId={promptId} from={Math.min(...selected)} to={Math.max(...selected)} />
      ) : (
        <Typography variant="caption" color="text.secondary">
          Select two versions above to compare them.
        </Typography>
      )}
    </Stack>
  )
}
