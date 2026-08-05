import CloseIcon from '@mui/icons-material/Close'
import { Alert, Box, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material'
import type { DocumentSummary } from '../api/documentsApi'
import { useDocument, useDocumentProcessingStatus } from '../hooks/useDocuments'
import { useDocumentProcessingHub } from '../hooks/useDocumentProcessingHub'
import { DocumentPreviewPane } from './DocumentPreviewPane'
import { MetadataPanel } from './MetadataPanel'
import { ProcessingHistoryPanel } from './ProcessingHistoryPanel'
import { ProcessingStatusBadge } from './ProcessingStatusBadge'
import { VersionTimeline } from './VersionTimeline'

interface DocumentDetailPanelProps {
  document: DocumentSummary | null
  onClose: () => void
}

/** Opened from a document row (US2 AC1/AC5); processing status/history/metadata/versions/preview all live here. */
export function DocumentDetailPanel({ document, onClose }: DocumentDetailPanelProps) {
  const open = document !== null
  const { data: status, isLoading, isError } = useDocumentProcessingStatus(document?.id ?? null)
  const { data: detail, isError: isDetailError } = useDocument(document?.id ?? null)
  const { isLive } = useDocumentProcessingHub(document?.id ?? null)

  return (
    <Drawer anchor="right" open={open} onClose={onClose}>
      <Box sx={{ width: { xs: '100vw', sm: 420 }, p: 3 }} role="dialog" aria-label={document ? `${document.fileName} details` : 'Document details'}>
        <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
          <Typography variant="h6" noWrap title={document?.fileName}>
            {document?.fileName}
          </Typography>
          <IconButton aria-label="Close" onClick={onClose}>
            <CloseIcon />
          </IconButton>
        </Stack>

        <Divider sx={{ mb: 2 }} />

        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Preview
        </Typography>
        {document && <DocumentPreviewPane documentId={document.id} fileType={document.fileType} />}

        <Divider sx={{ my: 3 }} />

        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Processing
        </Typography>

        {isError && <Alert severity="error">Could not load processing status. Please try again.</Alert>}
        {!isLoading && !isError && status && document && (
          <ProcessingStatusBadge documentId={document.id} status={status} isLive={isLive} />
        )}

        <Typography variant="subtitle2" sx={{ mt: 3, mb: 1 }}>
          History
        </Typography>
        {document && <ProcessingHistoryPanel documentId={document.id} />}

        <Divider sx={{ my: 3 }} />

        {isDetailError && <Alert severity="error">Could not load document details. Please try again.</Alert>}
        {!isDetailError && detail && document && <MetadataPanel documentId={document.id} document={detail} />}

        <Divider sx={{ my: 3 }} />

        <Typography variant="subtitle2" sx={{ mb: 1 }}>
          Versions
        </Typography>
        {document && <VersionTimeline documentId={document.id} />}
      </Box>
    </Drawer>
  )
}
