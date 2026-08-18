import CloseIcon from '@mui/icons-material/Close'
import CloudUploadIcon from '@mui/icons-material/CloudUpload'
import { Alert, Box, Button, IconButton, LinearProgress, Paper, Stack, Typography } from '@mui/material'
import { useEffect, useRef, useState } from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { useResumableUpload } from '../hooks/useResumableUpload'
import { DOCUMENTS_QUERY_KEY } from '../hooks/useDocuments'
import { radius } from '../../../theme'

interface UploadItemProps {
  file: File
  onRemove: () => void
}

/**
 * One row in the upload queue (FR-006's "overall queue view", plus per-file progress). Each
 * file gets its own {@link useResumableUpload} instance, chosen automatically between the
 * chunked and single-request paths by file size.
 */
function UploadItem({ file, onRemove }: UploadItemProps) {
  const { status, progress, error, start, cancel, resolveDuplicateAsVersion, resolveDuplicateAsNew } = useResumableUpload(file)
  const queryClient = useQueryClient()
  const startedRef = useRef(false)

  useEffect(() => {
    if (!startedRef.current) {
      startedRef.current = true
      start()
    }
  }, [start])

  useEffect(() => {
    if (status === 'completed') {
      queryClient.invalidateQueries({ queryKey: DOCUMENTS_QUERY_KEY })
    }
  }, [status, queryClient])

  return (
    <Paper variant="outlined" sx={{ p: 1.5 }}>
      <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
        <Typography variant="body2" noWrap sx={{ maxWidth: 280 }}>
          {file.name}
        </Typography>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
          <Typography variant="caption" color="text.secondary">
            {status === 'uploading' && `${progress}%`}
            {status === 'completed' && 'Done'}
            {status === 'cancelled' && 'Cancelled'}
            {status === 'error' && 'Failed'}
            {status === 'duplicate' && 'Duplicate found'}
          </Typography>
          {status === 'uploading' && (
            <IconButton size="small" aria-label={`Cancel upload of ${file.name}`} onClick={cancel}>
              <CloseIcon fontSize="small" />
            </IconButton>
          )}
          {(status === 'completed' || status === 'cancelled' || status === 'error') && (
            <IconButton size="small" aria-label={`Remove ${file.name} from the upload queue`} onClick={onRemove}>
              <CloseIcon fontSize="small" />
            </IconButton>
          )}
        </Stack>
      </Stack>

      {status === 'uploading' && <LinearProgress variant="determinate" value={progress} sx={{ mt: 1 }} />}

      {status === 'error' && (
        <Alert severity="error" sx={{ mt: 1 }}>
          {error}
        </Alert>
      )}

      {status === 'duplicate' && (
        <Alert
          severity="warning"
          sx={{ mt: 1 }}
          action={
            <Stack direction="row" spacing={1}>
              <Button size="small" onClick={() => resolveDuplicateAsVersion('Minor')}>
                Save as new version
              </Button>
              <Button size="small" onClick={resolveDuplicateAsNew}>
                Upload as separate file
              </Button>
            </Stack>
          }
        >
          A document with identical content already exists.
        </Alert>
      )}
    </Paper>
  )
}

/** Drag-and-drop (and click-to-browse) multi-file upload panel (FR-001–FR-004, FR-006, FR-007). */
export function UploadPanel() {
  const [queuedFiles, setQueuedFiles] = useState<{ id: string; file: File }[]>([])
  const [isDragOver, setIsDragOver] = useState(false)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const addFiles = (files: FileList | File[]) => {
    const next = Array.from(files).map((file) => ({ id: `${file.name}-${file.size}-${Date.now()}-${Math.random()}`, file }))
    setQueuedFiles((prev) => [...prev, ...next])
  }

  const removeFile = (id: string) => setQueuedFiles((prev) => prev.filter((f) => f.id !== id))

  return (
    <Box>
      <Box
        onDragOver={(e) => {
          e.preventDefault()
          setIsDragOver(true)
        }}
        onDragLeave={() => setIsDragOver(false)}
        onDrop={(e) => {
          e.preventDefault()
          setIsDragOver(false)
          if (e.dataTransfer.files.length > 0) addFiles(e.dataTransfer.files)
        }}
        onPaste={(e) => {
          const files = Array.from(e.clipboardData.files)
          if (files.length > 0) addFiles(files)
        }}
        onClick={() => fileInputRef.current?.click()}
        role="button"
        tabIndex={0}
        aria-label="Upload documents"
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') fileInputRef.current?.click()
        }}
        sx={{
          border: '2px dashed',
          borderColor: isDragOver ? 'primary.main' : 'divider',
          borderRadius: `${radius.lg}px`,
          p: 3,
          textAlign: 'center',
          cursor: 'pointer',
          bgcolor: isDragOver ? 'action.hover' : undefined,
          transition: (theme) => theme.transitions.create(['border-color', 'background-color']),
          '&:hover': { borderColor: 'primary.main' },
        }}
      >
        <CloudUploadIcon color="action" />
        <Typography variant="body2" color="text.secondary">
          Drag and drop documents here, paste, or click to browse
        </Typography>
        <input
          ref={fileInputRef}
          type="file"
          hidden
          multiple
          onChange={(e) => {
            if (e.target.files && e.target.files.length > 0) addFiles(e.target.files)
            e.target.value = ''
          }}
        />
      </Box>

      {queuedFiles.length > 0 && (
        <Stack spacing={1} sx={{ mt: 2 }}>
          {queuedFiles.map(({ id, file }) => (
            <UploadItem key={id} file={file} onRemove={() => removeFile(id)} />
          ))}
        </Stack>
      )}
    </Box>
  )
}
