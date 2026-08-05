import RefreshIcon from '@mui/icons-material/Refresh'
import { Alert, Box, Button, Chip, LinearProgress, Stack, Typography } from '@mui/material'
import type { DocumentProcessingStageType, DocumentProcessingStatusDto } from '../api/documentsApi'
import { useRetryProcessing } from '../hooks/useDocumentMutations'

const stageLabels: Record<DocumentProcessingStageType, string> = {
  Validation: 'Validating',
  Ocr: 'Running OCR',
  TextExtraction: 'Extracting text',
  MetadataExtraction: 'Extracting metadata',
  Classification: 'Classifying',
  LanguageDetection: 'Detecting language',
  PreviewGeneration: 'Generating preview',
}

interface ProcessingStatusBadgeProps {
  documentId: string
  status: DocumentProcessingStatusDto
  isLive: boolean
}

/** FR-012, FR-027, FR-028, FR-029, US2 AC1/AC2 — the current stage, a progress indicator, and (on Failed) a specific reason plus a working retry action. */
export function ProcessingStatusBadge({ documentId, status, isLive }: ProcessingStatusBadgeProps) {
  const retryProcessing = useRetryProcessing()

  const completedCount = status.stages.filter((s) => s.status === 'Completed' || s.status === 'Skipped').length
  const progressPercent = (completedCount / status.stages.length) * 100

  return (
    <Box>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
        <Chip
          size="small"
          label={status.processingStatus}
          color={status.processingStatus === 'Completed' ? 'success' : status.processingStatus === 'Failed' ? 'error' : 'info'}
        />
        <Typography variant="caption" color="text.secondary">
          {isLive ? 'Live' : 'Polling every 5s'}
        </Typography>
      </Stack>

      {status.processingStatus === 'Processing' && (
        <Box sx={{ mb: 1 }}>
          <LinearProgress variant="determinate" value={progressPercent} />
          <Typography variant="caption" color="text.secondary">
            {status.currentStage ? stageLabels[status.currentStage] : 'Processing'}…
          </Typography>
        </Box>
      )}

      {status.processingStatus === 'Failed' && (
        <Alert
          severity="error"
          action={
            <Button
              size="small"
              color="inherit"
              startIcon={<RefreshIcon fontSize="small" />}
              disabled={retryProcessing.isPending}
              onClick={() => retryProcessing.mutate(documentId)}
            >
              Retry
            </Button>
          }
        >
          {status.failureReason ?? 'Processing failed.'}
        </Alert>
      )}

      {retryProcessing.isError && (
        <Typography variant="caption" color="error" role="alert" component="div" sx={{ mt: 0.5 }}>
          {retryProcessing.error instanceof Error ? retryProcessing.error.message : 'Retry failed. Please try again.'}
        </Typography>
      )}
    </Box>
  )
}
