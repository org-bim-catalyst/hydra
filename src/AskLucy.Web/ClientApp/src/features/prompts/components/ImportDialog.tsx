import UploadFileIcon from '@mui/icons-material/UploadFile'
import { Alert, Box, Button, Dialog, DialogActions, DialogContent, DialogTitle, List, ListItem, ListItemText, Typography } from '@mui/material'
import { useRef, useState } from 'react'
import { ApiError } from '../../../api/httpClient'
import type { PromptExportFile } from '../api/promptsApi'
import { useImportPrompts } from '../hooks/usePromptMutations'

interface ImportDialogProps {
  open: boolean
  onClose: () => void
}

/**
 * Import dialog (spec.md FR-071/FR-072, User Story 7) — file picker, per-entry validation-error
 * display. Atomic: a `validation-failed` (400) response means nothing was created, so every listed
 * error refers to the *entire* attempted file, not partial progress.
 */
export function ImportDialog({ open, onClose }: ImportDialogProps) {
  const fileInputRef = useRef<HTMLInputElement>(null)
  const [fileName, setFileName] = useState<string | null>(null)
  const [parsedFile, setParsedFile] = useState<PromptExportFile | null>(null)
  const [parseError, setParseError] = useState<string | null>(null)
  const [entryErrors, setEntryErrors] = useState<string[] | null>(null)

  const importPrompts = useImportPrompts()

  const handleClose = () => {
    if (importPrompts.isPending) return
    setFileName(null)
    setParsedFile(null)
    setParseError(null)
    setEntryErrors(null)
    onClose()
  }

  const handleFileSelected = async (file: File) => {
    setFileName(file.name)
    setParseError(null)
    setEntryErrors(null)
    setParsedFile(null)
    try {
      const text = await file.text()
      setParsedFile(JSON.parse(text) as PromptExportFile)
    } catch {
      setParseError('This file is not valid JSON and could not be read.')
    }
  }

  const handleImport = () => {
    if (!parsedFile) return
    setEntryErrors(null)
    importPrompts.mutate(parsedFile, {
      onSuccess: handleClose,
      onError: (err) => {
        if (err instanceof ApiError && err.errors) {
          setEntryErrors(Object.values(err.errors).flat())
        } else {
          setEntryErrors([err instanceof Error ? err.message : 'Import failed. Please try again.'])
        }
      },
    })
  }

  return (
    <Dialog open={open} onClose={handleClose} fullWidth maxWidth="sm">
      <DialogTitle>Import prompts</DialogTitle>
      <DialogContent>
        <input
          ref={fileInputRef}
          type="file"
          accept="application/json,.json"
          hidden
          onChange={(e) => {
            const file = e.target.files?.[0]
            if (file) void handleFileSelected(file)
            e.target.value = ''
          }}
        />
        <Box sx={{ textAlign: 'center', py: 3 }}>
          <Button variant="outlined" startIcon={<UploadFileIcon />} onClick={() => fileInputRef.current?.click()}>
            Choose file…
          </Button>
          {fileName && (
            <Typography variant="body2" color="text.secondary" sx={{ mt: 1 }}>
              {fileName} — {parsedFile ? `${parsedFile.prompts.length} prompt(s)` : 'reading…'}
            </Typography>
          )}
        </Box>

        {parseError && <Alert severity="error">{parseError}</Alert>}

        {entryErrors && entryErrors.length > 0 && (
          <Alert severity="error" data-testid="import-validation-errors">
            <Typography variant="body2" sx={{ mb: 1 }}>
              The import was rejected — nothing was created. Fix every issue below and try again:
            </Typography>
            <List dense disablePadding>
              {entryErrors.map((message, index) => (
                <ListItem key={index} disableGutters>
                  <ListItemText primary={message} />
                </ListItem>
              ))}
            </List>
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose} disabled={importPrompts.isPending}>
          Cancel
        </Button>
        <Button variant="contained" onClick={handleImport} disabled={!parsedFile || importPrompts.isPending}>
          {importPrompts.isPending ? 'Importing…' : 'Import'}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
