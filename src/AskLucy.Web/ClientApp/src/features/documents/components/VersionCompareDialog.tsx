import CloseIcon from '@mui/icons-material/Close'
import { Box, Dialog, DialogContent, DialogTitle, IconButton, Stack, Typography } from '@mui/material'
import { useCompareVersions } from '../hooks/useDocuments'
import { ErrorState } from '../../../components/ErrorState'
import { SkeletonBlock } from '../../../components/SkeletonBlock'
import { codeFontFamily } from '../../../theme/tokens/typography'
import { radius } from '../../../theme'

interface VersionCompareDialogProps {
  documentId: string
  fromVersionId: string
  toVersionId: string
  onClose: () => void
}

function diffLineColor(line: string): string | undefined {
  if (line.startsWith('+ ')) return 'success.main'
  if (line.startsWith('- ')) return 'error.main'
  return undefined
}

/** FR-042, US5 AC — diffs extracted text and each version's own intrinsic fields (see CompareVersionsQueryHandler for why "metadata" here means the version row's own fields, not DocumentMetadata). */
export function VersionCompareDialog({ documentId, fromVersionId, toVersionId, onClose }: VersionCompareDialogProps) {
  const { data, isLoading, isError, refetch } = useCompareVersions(documentId, fromVersionId, toVersionId)

  return (
    <Dialog open onClose={onClose} maxWidth="md" fullWidth>
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        Compare versions
        <IconButton aria-label="Close" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </DialogTitle>
      <DialogContent dividers>
        {isError && <ErrorState title="Could not load the comparison" onRetry={() => void refetch()} />}
        {isLoading && <SkeletonBlock variant="text" count={4} />}

        {data && (
          <>
            {Object.keys(data.metadataDiff).length > 0 && (
              <Box sx={{ mb: 2 }}>
                <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
                  Changed fields
                </Typography>
                <Stack spacing={0.5}>
                  {Object.entries(data.metadataDiff).map(([field, diff]) => (
                    <Typography variant="body2" key={field}>
                      <strong>{field}</strong>: {diff.from ?? '—'} → {diff.to ?? '—'}
                    </Typography>
                  ))}
                </Stack>
              </Box>
            )}

            <Typography variant="subtitle2" sx={{ mb: 0.5 }}>
              Extracted text
            </Typography>
            <Box
              component="pre"
              sx={{
                fontFamily: codeFontFamily,
                fontSize: 13,
                whiteSpace: 'pre-wrap',
                overflowX: 'auto',
                bgcolor: 'action.hover',
                p: 1.5,
                borderRadius: `${radius.sm}px`,
                m: 0,
              }}
            >
              {data.extractedTextDiff
                ? data.extractedTextDiff.split('\n').map((line, index) => (
                    <Box key={`${index}-${line}`} component="span" sx={{ display: 'block', color: diffLineColor(line) }}>
                      {line}
                    </Box>
                  ))
                : 'No text differences.'}
            </Box>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}
