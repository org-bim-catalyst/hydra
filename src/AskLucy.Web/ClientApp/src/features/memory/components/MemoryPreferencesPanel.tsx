import DeleteForeverIcon from '@mui/icons-material/DeleteForever'
import { Alert, Box, Button, Divider, FormControlLabel, MenuItem, Snackbar, Stack, Switch, TextField, Typography } from '@mui/material'
import { useState } from 'react'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import type { MemoryApprovalMode, MemoryCategory } from '../api/memoryApi'
import { useMemoryPreferences } from '../hooks/useMemories'
import { useClearAllMemories, useUpdateMemoryPreferences } from '../hooks/useMemoryMutations'
import { MemoryExportButton } from './MemoryExportButton'

const CATEGORY_LABEL: Record<MemoryCategory, string> = {
  UserPreference: 'Preferences',
  PersonalFact: 'Personal facts',
  ProjectContext: 'Project context',
  ConversationDerived: 'Inferred from conversation',
}

const APPROVAL_MODE_OPTIONS: { value: MemoryApprovalMode; label: string }[] = [
  { value: 'Automatic', label: 'Automatic — remember without asking' },
  { value: 'Manual', label: 'Manual — review before remembering' },
  { value: 'Disabled', label: 'Disabled — never remember this category' },
]

/**
 * spec.md FR-007, FR-022–FR-025, User Story 3/4 — the account-level memory switch, per-category
 * approval mode/enablement, and the account-level privacy actions (export, clear all). Every
 * preference change takes effect immediately (no separate "Save" step), matching FR-022's
 * "immediate effect" framing. Clear-all reuses the shared `ConfirmDialog` (constitution §7 — a
 * destructive-action confirmation is exactly that component's existing purpose) rather than a
 * bespoke `ClearAllMemoriesDialog.tsx`.
 */
export function MemoryPreferencesPanel() {
  const { data: preferences, isLoading } = useMemoryPreferences()
  const updatePreferences = useUpdateMemoryPreferences()
  const clearAllMemories = useClearAllMemories()
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [clearAllOpen, setClearAllOpen] = useState(false)

  const reportError = (err: unknown) => setErrorMessage(err instanceof Error ? err.message : 'Save failed. Please try again.')

  if (isLoading || !preferences) {
    return null
  }

  return (
    <Box>
      <FormControlLabel
        control={
          <Switch
            checked={preferences.memoryEnabled}
            onChange={(e) => updatePreferences.mutate({ memoryEnabled: e.target.checked }, { onError: reportError })}
          />
        }
        label="Let Lucy remember things about me"
      />

      {preferences.memoryEnabled && (
        <Stack sx={{ mt: 2, gap: 2 }}>
          {preferences.categories.map((categoryPreference) => (
            <Stack key={categoryPreference.category} direction="row" spacing={2} sx={{ alignItems: 'center' }}>
              <Typography variant="body2" sx={{ minWidth: 200 }}>
                {CATEGORY_LABEL[categoryPreference.category]}
              </Typography>
              <TextField
                select
                size="small"
                slotProps={{ select: { 'aria-label': `${CATEGORY_LABEL[categoryPreference.category]} approval mode` } }}
                value={categoryPreference.approvalMode}
                sx={{ minWidth: 280 }}
                onChange={(e) =>
                  updatePreferences.mutate(
                    { categories: [{ category: categoryPreference.category, approvalMode: e.target.value as MemoryApprovalMode }] },
                    { onError: reportError },
                  )
                }
              >
                {APPROVAL_MODE_OPTIONS.map((option) => (
                  <MenuItem key={option.value} value={option.value}>
                    {option.label}
                  </MenuItem>
                ))}
              </TextField>
              <FormControlLabel
                control={
                  <Switch
                    size="small"
                    checked={categoryPreference.isEnabled}
                    onChange={(e) =>
                      updatePreferences.mutate(
                        { categories: [{ category: categoryPreference.category, isEnabled: e.target.checked }] },
                        { onError: reportError },
                      )
                    }
                  />
                }
                label="In use"
              />
            </Stack>
          ))}
        </Stack>
      )}

      <Divider sx={{ my: 3 }} />

      <Typography variant="subtitle2" gutterBottom>
        Your data
      </Typography>
      <Stack direction="row" spacing={1.5}>
        <MemoryExportButton />
        <Button variant="outlined" color="error" startIcon={<DeleteForeverIcon />} onClick={() => setClearAllOpen(true)}>
          Clear all memories
        </Button>
      </Stack>

      <ConfirmDialog
        open={clearAllOpen}
        title="Clear all memories?"
        description="Every memory Lucy has stored about you will be permanently removed. This cannot be undone."
        confirmLabel="Clear all"
        onCancel={() => setClearAllOpen(false)}
        onConfirm={() => {
          setClearAllOpen(false)
          clearAllMemories.mutate(undefined, { onError: reportError })
        }}
      />

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={5000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </Box>
  )
}
