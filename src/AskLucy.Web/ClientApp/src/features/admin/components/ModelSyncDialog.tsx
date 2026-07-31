import { useState } from 'react'
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogContentText,
  DialogTitle,
  List,
  ListItem,
  ListItemText,
  Snackbar,
  TextField,
  Typography,
} from '@mui/material'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { ApiError } from '../../../api/httpClient'
import * as adminAiProvidersApi from '../api/adminAiProvidersApi'
import type { AddedProviderModel, ApplyProviderModelSyncResult, ProviderModelSyncDiff, RemovedProviderModel } from '../api/adminAiProvidersApi'

interface ModelSyncDialogProps {
  providerId: string
  providerDisplayName: string
  open: boolean
  onClose: () => void
}

type Feedback = { severity: 'success' | 'error'; message: string } | null

const matchesFilter = (filterText: string) => (model: { displayName: string; modelKey: string }) => {
  const needle = filterText.trim().toLowerCase()
  if (!needle) return true
  return model.displayName.toLowerCase().includes(needle) || model.modelKey.toLowerCase().includes(needle)
}

/**
 * specs/008-ai-model-catalog-management US3 — "diff then apply", never an automatic,
 * unreviewed catalog change (FR-005-008/FR-010). The client echoes back exactly the
 * reviewed rows to `.../apply` — no server-side ephemeral cache, same pattern as spec
 * 005's model-comparison "continue" endpoint.
 *
 * specs/009-selective-model-sync-review adds: a single shared filter (FR-002) that
 * narrows both diff sides without ever changing which rows are selected (FR-005); per-row
 * selection with per-side select-all/none scoped to the currently-visible rows (FR-001/
 * FR-003/FR-004); a live selected count (FR-006); Confirm sends only the selected subset
 * (FR-007) and is disabled with nothing selected (FR-008/FR-013); the apply result (a
 * best-effort per-row outcome, FR-007a/FR-007b) is rendered in-dialog naming every failed
 * row with its reason (FR-012), mirroring AiProviderActionsMenu.tsx's Snackbar/Alert
 * convention for genuine request errors.
 */
export function ModelSyncDialog({ providerId, providerDisplayName, open, onClose }: ModelSyncDialogProps) {
  const queryClient = useQueryClient()
  const [diff, setDiff] = useState<ProviderModelSyncDiff | null>(null)
  const [filterText, setFilterText] = useState('')
  const [selectedAddedKeys, setSelectedAddedKeys] = useState<Set<string>>(new Set())
  const [selectedRemovedIds, setSelectedRemovedIds] = useState<Set<string>>(new Set())
  const [applyResult, setApplyResult] = useState<ApplyProviderModelSyncResult | null>(null)
  const [feedback, setFeedback] = useState<Feedback>(null)

  const onError = (err: unknown) => {
    const message = err instanceof ApiError ? err.detail ?? err.message : 'Something went wrong. Please try again.'
    setFeedback({ severity: 'error', message })
  }

  const resetReviewState = () => {
    setDiff(null)
    setFilterText('')
    setSelectedAddedKeys(new Set())
    setSelectedRemovedIds(new Set())
    setApplyResult(null)
  }

  const syncMutation = useMutation({
    mutationFn: () => adminAiProvidersApi.syncModels(providerId),
    onSuccess: (newDiff) => {
      setDiff(newDiff)
      setFilterText('')
      setSelectedAddedKeys(new Set())
      setSelectedRemovedIds(new Set())
    },
    onError,
  })

  const applyMutation = useMutation({
    mutationFn: (selection: ProviderModelSyncDiff) => adminAiProvidersApi.applyModelSync(providerId, selection),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['admin', 'ai-providers', providerId, 'models'] })
      setApplyResult(result)
    },
    onError,
  })

  const handleDismiss = () => {
    resetReviewState()
    onClose()
  }

  const handleClose = () => {
    resetReviewState()
    onClose()
  }

  const hasNothingToReview = diff !== null && diff.added.length === 0 && diff.removedFromVendor.length === 0

  const filteredAdded = diff?.added.filter(matchesFilter(filterText)) ?? []
  const filteredRemoved = diff?.removedFromVendor.filter(matchesFilter(filterText)) ?? []
  const addedFilterHasNoMatches = filterText.trim() !== '' && (diff?.added.length ?? 0) > 0 && filteredAdded.length === 0
  const removedFilterHasNoMatches = filterText.trim() !== '' && (diff?.removedFromVendor.length ?? 0) > 0 && filteredRemoved.length === 0

  const totalSelected = selectedAddedKeys.size + selectedRemovedIds.size

  const toggleAdded = (modelKey: string) =>
    setSelectedAddedKeys((prev) => {
      const next = new Set(prev)
      if (next.has(modelKey)) next.delete(modelKey)
      else next.add(modelKey)
      return next
    })

  const toggleRemoved = (id: string) =>
    setSelectedRemovedIds((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })

  const selectAllAdded = () => setSelectedAddedKeys((prev) => new Set([...prev, ...filteredAdded.map((m) => m.modelKey)]))
  const selectNoneAdded = () =>
    setSelectedAddedKeys((prev) => {
      const visibleKeys = new Set(filteredAdded.map((m) => m.modelKey))
      return new Set([...prev].filter((key) => !visibleKeys.has(key)))
    })

  const selectAllRemoved = () => setSelectedRemovedIds((prev) => new Set([...prev, ...filteredRemoved.map((m) => m.id)]))
  const selectNoneRemoved = () =>
    setSelectedRemovedIds((prev) => {
      const visibleIds = new Set(filteredRemoved.map((m) => m.id))
      return new Set([...prev].filter((id) => !visibleIds.has(id)))
    })

  const handleConfirmApply = () => {
    if (!diff) return
    const added: AddedProviderModel[] = diff.added.filter((m) => selectedAddedKeys.has(m.modelKey))
    const removedFromVendor: RemovedProviderModel[] = diff.removedFromVendor.filter((m) => selectedRemovedIds.has(m.id))
    applyMutation.mutate({ added, removedFromVendor })
  }

  return (
    <>
      <Dialog open={open} onClose={handleDismiss} maxWidth="sm" fullWidth>
        <DialogTitle>Sync {providerDisplayName}'s catalog from the provider</DialogTitle>
        <DialogContent>
          {applyResult && (
            <Box>
              {applyResult.appliedModelKeys.length > 0 && (
                <Alert severity="success" sx={{ mb: 2 }}>
                  Applied {applyResult.appliedModelKeys.length} model{applyResult.appliedModelKeys.length === 1 ? '' : 's'}.
                </Alert>
              )}
              {applyResult.failed.length > 0 && (
                <Box>
                  <Typography variant="subtitle2" color="error">
                    Could not apply {applyResult.failed.length} model{applyResult.failed.length === 1 ? '' : 's'}
                  </Typography>
                  <List dense>
                    {applyResult.failed.map((failure) => (
                      <ListItem key={failure.modelKey}>
                        <ListItemText primary={failure.displayName} secondary={failure.reason} />
                      </ListItem>
                    ))}
                  </List>
                </Box>
              )}
            </Box>
          )}
          {!applyResult && diff === null && (
            <DialogContentText>
              This checks the provider's own model list and shows you a diff to review — nothing changes until you confirm.
            </DialogContentText>
          )}
          {!applyResult && diff !== null && hasNothingToReview && (
            <DialogContentText>Nothing to review — the catalog already matches the provider.</DialogContentText>
          )}
          {!applyResult && diff !== null && !hasNothingToReview && (
            <Box>
              <TextField
                label="Filter by name or key"
                size="small"
                fullWidth
                value={filterText}
                onChange={(e) => setFilterText(e.target.value)}
                sx={{ mb: 1 }}
              />
              <Typography variant="body2" color="text.secondary" sx={{ mb: 2 }}>
                {totalSelected} selected
              </Typography>
              {diff.added.length > 0 && (
                <Box sx={{ mb: 2 }}>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Typography variant="subtitle2">New at the provider ({diff.added.length})</Typography>
                    <Box>
                      <Button size="small" onClick={selectAllAdded}>
                        Select all
                      </Button>
                      <Button size="small" onClick={selectNoneAdded}>
                        Select none
                      </Button>
                    </Box>
                  </Box>
                  {addedFilterHasNoMatches ? (
                    <Typography variant="body2" color="text.secondary">
                      No rows match your search.
                    </Typography>
                  ) : (
                    <List dense>
                      {filteredAdded.map((model) => (
                        <ListItem key={model.modelKey}>
                          <Checkbox
                            edge="start"
                            checked={selectedAddedKeys.has(model.modelKey)}
                            onChange={() => toggleAdded(model.modelKey)}
                            slotProps={{ input: { 'aria-label': `Select ${model.displayName}` } }}
                          />
                          <ListItemText primary={model.displayName} secondary={model.modelKey} />
                          <Chip size="small" label="Will be added as Unavailable" variant="outlined" />
                        </ListItem>
                      ))}
                    </List>
                  )}
                </Box>
              )}
              {diff.removedFromVendor.length > 0 && (
                <Box>
                  <Box sx={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center' }}>
                    <Typography variant="subtitle2">No longer listed by the provider ({diff.removedFromVendor.length})</Typography>
                    <Box>
                      <Button size="small" onClick={selectAllRemoved}>
                        Select all
                      </Button>
                      <Button size="small" onClick={selectNoneRemoved}>
                        Select none
                      </Button>
                    </Box>
                  </Box>
                  {removedFilterHasNoMatches ? (
                    <Typography variant="body2" color="text.secondary">
                      No rows match your search.
                    </Typography>
                  ) : (
                    <List dense>
                      {filteredRemoved.map((model) => (
                        <ListItem key={model.id}>
                          <Checkbox
                            edge="start"
                            checked={selectedRemovedIds.has(model.id)}
                            onChange={() => toggleRemoved(model.id)}
                            slotProps={{ input: { 'aria-label': `Select ${model.displayName}` } }}
                          />
                          <ListItemText primary={model.displayName} secondary={model.modelKey} />
                          <Chip size="small" label="Will be marked Unavailable" variant="outlined" color="warning" />
                        </ListItem>
                      ))}
                    </List>
                  )}
                </Box>
              )}
            </Box>
          )}
        </DialogContent>
        <DialogActions>
          {applyResult ? (
            <Button onClick={handleClose} variant="contained" autoFocus>
              Close
            </Button>
          ) : (
            <>
              <Button onClick={handleDismiss}>{diff === null ? 'Cancel' : 'Dismiss'}</Button>
              {diff === null && (
                <Button onClick={() => syncMutation.mutate()} variant="contained" disabled={syncMutation.isPending}>
                  Check for updates
                </Button>
              )}
              {diff !== null && !hasNothingToReview && (
                <Button
                  onClick={handleConfirmApply}
                  variant="contained"
                  color="warning"
                  disabled={applyMutation.isPending || totalSelected === 0}
                  autoFocus
                >
                  Confirm
                </Button>
              )}
            </>
          )}
        </DialogActions>
      </Dialog>

      <Snackbar open={feedback !== null} autoHideDuration={5000} onClose={() => setFeedback(null)}>
        <Alert severity={feedback?.severity ?? 'info'} variant="filled" onClose={() => setFeedback(null)}>
          {feedback?.message}
        </Alert>
      </Snackbar>
    </>
  )
}
