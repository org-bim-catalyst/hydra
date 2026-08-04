import CloudUploadIcon from '@mui/icons-material/CloudUpload'
import { Alert, Box, Button, LinearProgress, Snackbar, Typography } from '@mui/material'
import { useRef, useState } from 'react'
import { useUploadDocument } from '../hooks/useKnowledgeBaseFolders'

interface DocumentUploadZoneProps {
  knowledgeBaseId: string
  /** Uploads land in this folder; null = the knowledge base's root (FR-012–FR-016). */
  targetFolderId: string | null
}

/**
 * Drag-and-drop (and click-to-browse) file upload (FR-014). Content is validated server-side
 * by magic-byte signature (constitution §8) — this component never trusts the browser's
 * reported MIME type or file extension for anything beyond the initial upload attempt; a
 * rejection surfaces the server's specific reason via the error Snackbar (constitution
 * §2.VIII No Silent Failures), not a generic "upload failed."
 */
export function DocumentUploadZone({ knowledgeBaseId, targetFolderId }: DocumentUploadZoneProps) {
  const uploadDocument = useUploadDocument(knowledgeBaseId)
  const [isDragOver, setIsDragOver] = useState(false)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)

  const upload = (file: File) => {
    uploadDocument.mutate(
      { file, folderId: targetFolderId },
      { onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Upload failed. Please try again.') },
    )
  }

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
          const file = e.dataTransfer.files[0]
          if (file) upload(file)
        }}
        onClick={() => fileInputRef.current?.click()}
        role="button"
        tabIndex={0}
        aria-label="Upload a document"
        onKeyDown={(e) => {
          if (e.key === 'Enter' || e.key === ' ') fileInputRef.current?.click()
        }}
        sx={{
          border: '2px dashed',
          borderColor: isDragOver ? 'primary.main' : 'divider',
          borderRadius: 1,
          p: 3,
          textAlign: 'center',
          cursor: 'pointer',
          bgcolor: isDragOver ? 'action.hover' : undefined,
        }}
      >
        <CloudUploadIcon color="action" />
        <Typography variant="body2" color="text.secondary">
          Drag and drop a document here, or click to browse
        </Typography>
        <Typography variant="caption" color="text.secondary">
          PDF, Word, Excel, PowerPoint, Markdown, CSV, or Text
        </Typography>
        <input
          ref={fileInputRef}
          type="file"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) upload(file)
            e.target.value = ''
          }}
        />
      </Box>

      {uploadDocument.isPending && <LinearProgress sx={{ mt: 1 }} />}

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={6000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>

      {/* Kept for parity with the shared component-library conventions elsewhere (e.g. ProfilePage's avatar upload) — a visible, non-drag fallback action. */}
      <Button size="small" sx={{ mt: 1 }} onClick={() => fileInputRef.current?.click()}>
        Choose file
      </Button>
    </Box>
  )
}
