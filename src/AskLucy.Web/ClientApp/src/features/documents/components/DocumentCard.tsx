import ArchiveIcon from '@mui/icons-material/Archive'
import ContentCopyIcon from '@mui/icons-material/ContentCopy'
import DeleteIcon from '@mui/icons-material/Delete'
import DownloadIcon from '@mui/icons-material/Download'
import DriveFileMoveIcon from '@mui/icons-material/DriveFileMove'
import RestoreIcon from '@mui/icons-material/Restore'
import UnarchiveIcon from '@mui/icons-material/Unarchive'
import { Card, CardContent, Chip, IconButton, Menu, MenuItem, Stack, Tooltip, Typography } from '@mui/material'
import { useState } from 'react'
import type { DocumentSummary } from '../api/documentsApi'
import { downloadDocument } from '../api/documentsApi'
import { useFolderTree } from '../hooks/useDocuments'
import {
  useArchiveDocument,
  useDeleteDocument,
  useDuplicateDocument,
  useMoveDocument,
  useRestoreDocument,
} from '../hooks/useDocumentMutations'

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }
  return `${value.toFixed(1)} ${units[unitIndex]}`
}

const statusColor: Record<DocumentSummary['processingStatus'], 'default' | 'info' | 'success' | 'error'> = {
  Uploaded: 'default',
  Queued: 'info',
  Processing: 'info',
  Completed: 'success',
  Failed: 'error',
}

interface DocumentCardProps {
  document: DocumentSummary
  view: 'Active' | 'Archived' | 'Deleted'
  onOpenDetail: (document: DocumentSummary) => void
}

/** One document in the workspace grid/list (User Story 1). Status is always paired with a text label, never color alone (accessibility, FR-052). Clicking the title opens DocumentDetailPanel (US2 AC1/AC5). */
export function DocumentCard({ document, view, onOpenDetail }: DocumentCardProps) {
  const archiveDocument = useArchiveDocument()
  const restoreDocument = useRestoreDocument()
  const deleteDocument = useDeleteDocument()
  const duplicateDocument = useDuplicateDocument()
  const moveDocument = useMoveDocument()
  const { data: folders } = useFolderTree()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [moveMenuAnchor, setMoveMenuAnchor] = useState<HTMLElement | null>(null)

  const handleError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  return (
    <Card
      variant="outlined"
      sx={{
        transition: (theme) => theme.transitions.create('box-shadow'),
        '&:hover': { boxShadow: (theme) => theme.shadows[2] },
      }}
    >
      <CardContent>
        <Typography
          component="span"
          variant="subtitle2"
          noWrap
          title={document.fileName}
          onClick={() => onOpenDetail(document)}
          role="button"
          tabIndex={0}
          aria-label={`Open details for ${document.fileName}`}
          onKeyDown={(e) => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault()
              onOpenDetail(document)
            }
          }}
          sx={{ display: 'block', cursor: 'pointer', '&:hover': { textDecoration: 'underline' } }}
        >
          {document.fileName}
        </Typography>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mt: 0.5, mb: 1 }}>
          <Chip size="small" label={document.processingStatus} color={statusColor[document.processingStatus]} />
          <Typography variant="caption" color="text.secondary">
            {formatBytes(document.sizeBytes)}
          </Typography>
        </Stack>

        {errorMessage && (
          <Typography variant="caption" color="error" component="div" sx={{ mb: 1 }} role="alert">
            {errorMessage}
          </Typography>
        )}

        <Stack direction="row" spacing={0.5}>
          <Tooltip title="Download">
            <IconButton size="small" aria-label={`Download ${document.fileName}`} onClick={() => downloadDocument(document.id).catch(handleError)}>
              <DownloadIcon fontSize="small" />
            </IconButton>
          </Tooltip>

          {view !== 'Deleted' && !document.isArchived && (
            <Tooltip title="Archive">
              <IconButton
                size="small"
                aria-label={`Archive ${document.fileName}`}
                onClick={() => archiveDocument.mutate(document.id, { onError: handleError })}
              >
                <ArchiveIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}

          {(document.isArchived || view === 'Deleted') && (
            <Tooltip title="Restore">
              <IconButton
                size="small"
                aria-label={`Restore ${document.fileName}`}
                onClick={() => restoreDocument.mutate(document.id, { onError: handleError })}
              >
                {document.isArchived ? <UnarchiveIcon fontSize="small" /> : <RestoreIcon fontSize="small" />}
              </IconButton>
            </Tooltip>
          )}

          {view !== 'Deleted' && (
            <Tooltip title="Delete">
              <IconButton
                size="small"
                aria-label={`Delete ${document.fileName}`}
                onClick={() => deleteDocument.mutate(document.id, { onError: handleError })}
              >
                <DeleteIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}

          {view !== 'Deleted' && (
            <Tooltip title="Duplicate">
              <IconButton
                size="small"
                aria-label={`Duplicate ${document.fileName}`}
                onClick={() => duplicateDocument.mutate(document.id, { onError: handleError })}
              >
                <ContentCopyIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          )}

          {view !== 'Deleted' && (
            <>
              <Tooltip title="Move to folder">
                <IconButton
                  size="small"
                  aria-label={`Move ${document.fileName} to a folder`}
                  onClick={(e) => setMoveMenuAnchor(e.currentTarget)}
                >
                  <DriveFileMoveIcon fontSize="small" />
                </IconButton>
              </Tooltip>
              <Menu anchorEl={moveMenuAnchor} open={Boolean(moveMenuAnchor)} onClose={() => setMoveMenuAnchor(null)}>
                <MenuItem
                  onClick={() => {
                    moveDocument.mutate({ id: document.id, folderId: null }, { onError: handleError })
                    setMoveMenuAnchor(null)
                  }}
                >
                  Root (no folder)
                </MenuItem>
                {folders?.map((folder) => (
                  <MenuItem
                    key={folder.id}
                    onClick={() => {
                      moveDocument.mutate({ id: document.id, folderId: folder.id }, { onError: handleError })
                      setMoveMenuAnchor(null)
                    }}
                  >
                    {folder.name}
                  </MenuItem>
                ))}
              </Menu>
            </>
          )}
        </Stack>
      </CardContent>
    </Card>
  )
}
