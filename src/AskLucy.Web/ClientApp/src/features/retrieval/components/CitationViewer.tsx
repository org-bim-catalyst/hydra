import { Button, Dialog, DialogActions, DialogContent, DialogTitle, Stack, Typography } from '@mui/material'
import type { Citation } from '../../chat/api/aiApi'

/** specs/016-rag-semantic-search US1 T056 — the source document at the cited page/section, with the retrieved passage set apart visually ("highlighted"). */
export function CitationViewer({ citation, open, onClose }: { citation: Citation; open: boolean; onClose: () => void }) {
  const locationLabel = [
    citation.pageNumber != null ? `Page ${citation.pageNumber}` : null,
    citation.section ?? null,
  ]
    .filter(Boolean)
    .join(' · ')

  return (
    <Dialog open={open} onClose={onClose} maxWidth="sm" fullWidth>
      <DialogTitle>{citation.sourceLabel}</DialogTitle>
      <DialogContent>
        <Stack spacing={1.5}>
          {locationLabel && (
            <Typography variant="caption" color="text.secondary">
              {locationLabel}
            </Typography>
          )}
          {citation.excerpt ? (
            <Typography
              variant="body2"
              component="blockquote"
              sx={{
                m: 0,
                p: 1.5,
                whiteSpace: 'pre-wrap',
                bgcolor: 'warning.light',
                color: 'warning.contrastText',
                borderLeft: 4,
                borderColor: 'warning.main',
                borderRadius: 1,
              }}
            >
              {citation.excerpt}
            </Typography>
          ) : (
            <Typography variant="body2" color="text.secondary">
              The retrieved passage is no longer available to preview, but this citation is preserved on the original response.
            </Typography>
          )}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  )
}
