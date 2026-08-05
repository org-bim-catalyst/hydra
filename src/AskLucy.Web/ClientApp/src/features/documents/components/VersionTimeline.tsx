import CompareArrowsIcon from '@mui/icons-material/CompareArrows'
import UploadFileIcon from '@mui/icons-material/UploadFile'
import { Alert, Box, Button, Chip, LinearProgress, List, ListItem, ListItemText, MenuItem, Stack, TextField, Typography } from '@mui/material'
import { useRef, useState } from 'react'
import { useVersionTimeline } from '../hooks/useDocuments'
import { useRestoreDocumentVersion } from '../hooks/useDocumentMutations'
import { useReplaceDocument } from '../hooks/useReplaceDocument'
import { VersionCompareDialog } from './VersionCompareDialog'

interface VersionTimelineProps {
  documentId: string
}

/** FR-038–FR-041, US5 — replace the current file (creating a new version), view the timeline, compare two versions, and restore an earlier one. */
export function VersionTimeline({ documentId }: VersionTimelineProps) {
  const { data: versions, isLoading, isError } = useVersionTimeline(documentId)
  const restoreVersion = useRestoreDocumentVersion()
  const replaceDocument = useReplaceDocument(documentId)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [versionIncrement, setVersionIncrement] = useState<'Major' | 'Minor'>('Minor')
  const [compareIds, setCompareIds] = useState<{ from: string; to: string } | null>(null)
  const [restoreError, setRestoreError] = useState<string | null>(null)

  const handleFileSelected = (event: React.ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0]
    if (file) {
      replaceDocument.start(file, versionIncrement)
    }
    event.target.value = ''
  }

  const handleRestore = (versionId: string) => {
    setRestoreError(null)
    restoreVersion.mutate(
      { documentId, versionId },
      { onError: (err) => setRestoreError(err instanceof Error ? err.message : 'Restore failed. Please try again.') },
    )
  }

  if (isError) {
    return <Alert severity="error">Could not load the version history. Please try again.</Alert>
  }

  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1.5 }}>
        <TextField
          select
          size="small"
          label="Increment"
          value={versionIncrement}
          onChange={(e) => setVersionIncrement(e.target.value as 'Major' | 'Minor')}
          sx={{ minWidth: 110 }}
        >
          <MenuItem value="Minor">Minor</MenuItem>
          <MenuItem value="Major">Major</MenuItem>
        </TextField>
        <Button
          size="small"
          variant="outlined"
          startIcon={<UploadFileIcon fontSize="small" />}
          onClick={() => fileInputRef.current?.click()}
          disabled={replaceDocument.status === 'uploading'}
        >
          Replace file
        </Button>
        <input ref={fileInputRef} type="file" hidden onChange={handleFileSelected} />
      </Stack>

      {replaceDocument.status === 'uploading' && (
        <Box sx={{ mb: 1.5 }}>
          <LinearProgress variant="determinate" value={replaceDocument.progress} />
        </Box>
      )}
      {replaceDocument.status === 'error' && (
        <Alert severity="error" sx={{ mb: 1.5 }} onClose={replaceDocument.reset}>
          {replaceDocument.error}
        </Alert>
      )}
      {restoreError && (
        <Alert severity="error" sx={{ mb: 1.5 }} onClose={() => setRestoreError(null)}>
          {restoreError}
        </Alert>
      )}

      {!isLoading && (versions?.length ?? 0) === 0 && (
        <Typography variant="body2" color="text.secondary">
          No version history yet.
        </Typography>
      )}

      <List dense>
        {versions?.map((version, index) => (
          <ListItem
            key={version.id}
            disableGutters
            secondaryAction={
              <Stack direction="row" spacing={0.5}>
                {index < versions.length - 1 && (
                  <Button
                    size="small"
                    startIcon={<CompareArrowsIcon fontSize="small" />}
                    onClick={() => setCompareIds({ from: versions[index + 1].id, to: version.id })}
                  >
                    Compare
                  </Button>
                )}
                {!version.isCurrent && (
                  <Button size="small" onClick={() => handleRestore(version.id)} disabled={restoreVersion.isPending}>
                    Restore
                  </Button>
                )}
              </Stack>
            }
          >
            <ListItemText
              primary={
                <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
                  <span>{`v${version.versionLabel}`}</span>
                  {version.isCurrent && <Chip size="small" label="Current" color="success" />}
                </Stack>
              }
              secondary={`${new Date(version.createdAtUtc).toLocaleString()} · ${version.createdByUserId} · ${(version.sizeBytes / 1024).toFixed(1)} KB`}
            />
          </ListItem>
        ))}
      </List>

      {compareIds && (
        <VersionCompareDialog
          documentId={documentId}
          fromVersionId={compareIds.from}
          toVersionId={compareIds.to}
          onClose={() => setCompareIds(null)}
        />
      )}
    </Box>
  )
}
