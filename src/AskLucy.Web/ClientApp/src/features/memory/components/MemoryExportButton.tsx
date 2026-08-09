import DownloadIcon from '@mui/icons-material/Download'
import { Alert, Button, Snackbar } from '@mui/material'
import { useEffect, useState } from 'react'
import { useMemoryExportStatus } from '../hooks/useMemories'
import { useRequestMemoryExport } from '../hooks/useMemoryMutations'

/**
 * spec.md FR-024, User Story 4 AC3 — requests a background export, polls until it's `Ready`,
 * then triggers the browser download from the signed URL. An account with zero memories still
 * gets a valid (empty) export, not an error (spec.md Edge Cases).
 */
export function MemoryExportButton() {
  const requestExport = useRequestMemoryExport()
  const [exportJobId, setExportJobId] = useState<string | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const { data: status } = useMemoryExportStatus(exportJobId)

  useEffect(() => {
    if (status?.status === 'Ready' && status.downloadUrl) {
      window.location.assign(status.downloadUrl)
      setExportJobId(null)
    } else if (status?.status === 'Failed') {
      setErrorMessage('Export failed. Please try again.')
      setExportJobId(null)
    }
  }, [status])

  const handleClick = () => {
    requestExport.mutate(undefined, {
      onSuccess: (result) => setExportJobId(result.exportJobId),
      onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Export failed. Please try again.'),
    })
  }

  const isBusy = requestExport.isPending || (exportJobId !== null && status?.status !== 'Ready' && status?.status !== 'Failed')

  return (
    <>
      <Button variant="outlined" startIcon={<DownloadIcon />} onClick={handleClick} loading={isBusy}>
        Export my memories
      </Button>

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={5000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </>
  )
}
