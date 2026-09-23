import { useState } from 'react'
import {
  Alert,
  Button,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
} from '@mui/material'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { TableEmptyRow } from '../../../../components/TableEmptyRow'
import { TableLoadingRow } from '../../../../components/TableLoadingRow'
import * as customModelsApi from '../../api/adminCustomModelsApi'
import { formatBytes } from './formatBytes'
import { errorMessage } from './errorMessage'

const PAGE_SIZE = 100

interface OverwrittenFilesDialogProps {
  open: boolean
  modelId: string
  modelName: string
  onClose: () => void
}

/** specs/072 FR-015 — the files a deployment replaced on the target, with their previous sizes. */
export function OverwrittenFilesDialog({ open, modelId, modelName, onClose }: OverwrittenFilesDialogProps) {
  const [page, setPage] = useState(1)

  const detailQuery = useQuery({
    queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.detail(modelId, page),
    queryFn: () => customModelsApi.getCustomModel(modelId, page, PAGE_SIZE),
    enabled: open,
    placeholderData: keepPreviousData,
  })
  const files = detailQuery.data?.overwrittenFiles

  return (
    <Dialog open={open} onClose={onClose} maxWidth="md" fullWidth aria-labelledby="overwritten-files-title">
      <DialogTitle id="overwritten-files-title">Files overwritten by {modelName}</DialogTitle>
      <DialogContent>
        {detailQuery.isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={
              <Button color="inherit" size="small" onClick={() => void detailQuery.refetch()}>
                Retry
              </Button>
            }
          >
            {errorMessage(detailQuery.error)}
          </Alert>
        )}
        <Table size="small" aria-labelledby="overwritten-files-title">
          <TableHead>
            <TableRow>
              <TableCell>File</TableCell>
              <TableCell align="right">Previous size</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {detailQuery.isLoading && <TableLoadingRow colSpan={2} rows={3} />}
            {files && files.items.length === 0 && <TableEmptyRow colSpan={2} message="No files were overwritten." />}
            {files?.items.map((file) => (
              <TableRow key={file.relativePath}>
                <TableCell sx={{ wordBreak: 'break-all' }}>{file.relativePath}</TableCell>
                <TableCell align="right">{formatBytes(file.previousSizeBytes)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
        {files && files.totalCount > files.pageSize && (
          <TablePagination
            component="div"
            count={files.totalCount}
            page={files.page - 1}
            rowsPerPage={files.pageSize}
            rowsPerPageOptions={[]}
            onPageChange={(_, next) => setPage(next + 1)}
          />
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>Close</Button>
      </DialogActions>
    </Dialog>
  )
}
