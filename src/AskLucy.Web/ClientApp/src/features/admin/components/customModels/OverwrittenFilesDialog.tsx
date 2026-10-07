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
import { useT } from '../../../../i18n/useT'
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
export function OverwrittenFilesDialog({
  open,
  modelId,
  modelName,
  onClose,
}: OverwrittenFilesDialogProps) {
  const t = useT('admin.aiProviders')
  const [page, setPage] = useState(1)

  const detailQuery = useQuery({
    queryKey: customModelsApi.CUSTOM_MODELS_QUERY_KEYS.detail(modelId, page),
    queryFn: () => customModelsApi.getCustomModel(modelId, page, PAGE_SIZE),
    enabled: open,
    placeholderData: keepPreviousData,
  })
  const files = detailQuery.data?.overwrittenFiles

  return (
    <Dialog
      open={open}
      onClose={onClose}
      maxWidth="md"
      fullWidth
      aria-labelledby="overwritten-files-title"
    >
      <DialogTitle id="overwritten-files-title">
        {t('overwritten.title', { name: modelName })}
      </DialogTitle>
      <DialogContent>
        {detailQuery.isError && (
          <Alert
            severity="error"
            sx={{ mb: 2 }}
            action={
              <Button color="inherit" size="small" onClick={() => void detailQuery.refetch()}>
                {t('shared.retry')}
              </Button>
            }
          >
            {errorMessage(detailQuery.error, t)}
          </Alert>
        )}
        <Table size="small" aria-labelledby="overwritten-files-title">
          <TableHead>
            <TableRow>
              <TableCell>{t('overwritten.file')}</TableCell>
              <TableCell sx={{ textAlign: 'end' }}>{t('overwritten.previousSize')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {detailQuery.isLoading && <TableLoadingRow colSpan={2} rows={3} />}
            {files && files.items.length === 0 && (
              <TableEmptyRow colSpan={2} message={t('overwritten.empty')} />
            )}
            {files?.items.map((file) => (
              <TableRow key={file.relativePath}>
                <TableCell sx={{ wordBreak: 'break-all' }}>
                  <bdi dir="ltr">{file.relativePath}</bdi>
                </TableCell>
                <TableCell sx={{ textAlign: 'end' }}>
                  <bdi dir="ltr">{formatBytes(file.previousSizeBytes, t('shared.bytes'))}</bdi>
                </TableCell>
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
            labelDisplayedRows={({ from, to, count }) =>
              t('overwritten.displayedRows', { from, to, count })
            }
            getItemAriaLabel={(type) =>
              ({
                first: t('overwritten.goToFirst'),
                last: t('overwritten.goToLast'),
                next: t('overwritten.goToNext'),
                previous: t('overwritten.goToPrevious'),
              })[type]
            }
            onPageChange={(_, next) => setPage(next + 1)}
          />
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose}>{t('shared.close')}</Button>
      </DialogActions>
    </Dialog>
  )
}
