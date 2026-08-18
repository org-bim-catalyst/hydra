import SearchIcon from '@mui/icons-material/Search'
import {
  Alert,
  Button,
  Checkbox,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  InputAdornment,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  TextField,
  Typography,
} from '@mui/material'
import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import * as promptsApi from '../api/promptsApi'
import { useExportPrompts } from '../hooks/usePromptMutations'

interface ExportDialogProps {
  open: boolean
  onClose: () => void
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

/** Export dialog (spec.md FR-070, User Story 7) — multi-select from the library, downloads a portable JSON file. */
export function ExportDialog({ open, onClose }: ExportDialogProps) {
  const [query, setQuery] = useState('')
  const [selectedIds, setSelectedIds] = useState<string[]>([])
  const [error, setError] = useState<string | null>(null)

  const { data } = useQuery({
    queryKey: ['prompts', 'list', 'export-picker', query],
    queryFn: () => promptsApi.listPrompts({ view: 'All', q: query.trim() || undefined, pageSize: 50 }),
    enabled: open,
  })
  const exportPrompts = useExportPrompts()

  const toggle = (id: string) => setSelectedIds((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]))

  const handleClose = () => {
    if (exportPrompts.isPending) return
    setQuery('')
    setSelectedIds([])
    setError(null)
    onClose()
  }

  const handleExport = () => {
    setError(null)
    exportPrompts.mutate(selectedIds, {
      onSuccess: (blob) => {
        downloadBlob(blob, selectedIds.length === 1 ? 'prompt-export.json' : 'prompts-export.json')
        handleClose()
      },
      onError: (err) => setError(err instanceof Error ? err.message : 'Export failed. Please try again.'),
    })
  }

  return (
    <Dialog open={open} onClose={handleClose} fullWidth maxWidth="sm">
      <DialogTitle>Export prompts</DialogTitle>
      <DialogContent>
        <TextField
          autoFocus
          fullWidth
          size="small"
          placeholder="Search prompts…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
          }}
          sx={{ mb: 2 }}
        />
        <List data-testid="export-prompt-list">
          {(data?.items ?? []).map((prompt) => (
            <ListItemButton key={prompt.id} onClick={() => toggle(prompt.id)} dense>
              <ListItemIcon sx={{ minWidth: 36 }}>
                <Checkbox edge="start" checked={selectedIds.includes(prompt.id)} tabIndex={-1} disableRipple />
              </ListItemIcon>
              <ListItemText primary={prompt.name} secondary={prompt.description} />
            </ListItemButton>
          ))}
          {data?.items.length === 0 && (
            <Typography variant="body2" color="text.secondary" sx={{ p: 2 }}>
              No prompts found.
            </Typography>
          )}
        </List>
        {error && (
          <Alert severity="error" sx={{ mt: 1 }}>
            {error}
          </Alert>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={handleClose} disabled={exportPrompts.isPending}>
          Cancel
        </Button>
        <Button variant="contained" onClick={handleExport} disabled={selectedIds.length === 0 || exportPrompts.isPending}>
          {exportPrompts.isPending ? 'Exporting…' : `Export${selectedIds.length > 0 ? ` (${selectedIds.length})` : ''}`}
        </Button>
      </DialogActions>
    </Dialog>
  )
}
