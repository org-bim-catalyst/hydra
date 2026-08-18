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
    // Deferred via queueMicrotask (react-hooks/set-state-in-effect): these setState calls
    // react to the export job's polled status, not to a value derived from render, so they
    // belong in an effect — but the rule wants the update to land in a callback rather than
    // synchronously in the effect body, to avoid a same-commit cascading render.
    if (status?.status === 'Ready' && status.downloadUrl) {
      window.location.assign(status.downloadUrl)
      queueMicrotask(() => setExportJobId(null))
    } else if (status?.status === 'Failed') {
      queueMicrotask(() => {
        setErrorMessage('Export failed. Please try again.')
        setExportJobId(null)
      })
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
