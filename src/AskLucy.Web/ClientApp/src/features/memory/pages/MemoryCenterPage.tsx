import SearchIcon from '@mui/icons-material/Search'
import { Alert, InputAdornment, MenuItem, Snackbar, Stack, Tab, Tabs, TextField } from '@mui/material'
import { useMemo, useState } from 'react'
import { AppShell } from '../../../components/AppShell'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import type { MemoryCategory, MemoryLifecycleState, MemoryListItem } from '../api/memoryApi'
import { MemoryApprovalQueue } from '../components/MemoryApprovalQueue'
import { MemoryEditDialog } from '../components/MemoryEditDialog'
import { MemoryList } from '../components/MemoryList'
import { MemoryNotificationList } from '../components/MemoryNotificationList'
import { MemoryPreferencesPanel } from '../components/MemoryPreferencesPanel'
import { ProjectManagementPanel } from '../components/ProjectManagementPanel'
import { useMemories } from '../hooks/useMemories'
import { useDeleteMemory, useEditMemory } from '../hooks/useMemoryMutations'
import { useMemoryNotificationsHub } from '../hooks/useMemoryNotificationsHub'
import { useMemoryCenterStore } from '../store/memoryCenterStore'

const CATEGORY_OPTIONS: { value: MemoryCategory; label: string }[] = [
  { value: 'UserPreference', label: 'Preference' },
  { value: 'PersonalFact', label: 'Personal fact' },
  { value: 'ProjectContext', label: 'Project context' },
  { value: 'ConversationDerived', label: 'From conversation' },
]

const STATE_OPTIONS: { value: MemoryLifecycleState; label: string }[] = [
  { value: 'PendingApproval', label: 'Pending approval' },
  { value: 'Active', label: 'Active' },
  { value: 'Archived', label: 'Archived' },
]

type MemoryCenterTab = 'all' | 'approvals' | 'preferences' | 'notifications' | 'projects'

/**
 * The Memory Center (spec.md FR-017–FR-025, User Stories 2/3) — every memory Lucy has stored,
 * searchable/filterable, editable (with history), and deletable (quickstart.md Scenario 2),
 * plus the approval queue, per-category preferences, and notification feed (User Story 3).
 * Uses the shared `ConfirmDialog` for delete confirmation rather than a bespoke dialog
 * (constitution §7 — a delete confirmation is exactly this component's existing purpose).
 */
export function MemoryCenterPage() {
  const { query, category, state, setQuery, setCategory, setState } = useMemoryCenterStore()
  const [tab, setTab] = useState<MemoryCenterTab>('all')

  // Established once per page visit (mirrors DocumentWorkspacePage's useNotificationHub usage) —
  // the poll fallback (useMemoryNotifications inside MemoryNotificationList) covers anything
  // missed while this connection was down or the tab wasn't mounted at all.
  useMemoryNotificationsHub()

  const isFiltered = query.trim() !== '' || Boolean(category) || Boolean(state)

  const searchParams = {
    category: (category as MemoryCategory | null) ?? undefined,
    state: (state as MemoryLifecycleState | null) ?? undefined,
    query: query.trim() || undefined,
  }

  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage } = useMemories(searchParams)
  const editMemory = useEditMemory()
  const deleteMemory = useDeleteMemory()

  const [editTarget, setEditTarget] = useState<MemoryListItem | null>(null)
  const [editErrorMessage, setEditErrorMessage] = useState<string | null>(null)
  const [deleteTarget, setDeleteTarget] = useState<MemoryListItem | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const memories = useMemo(() => data?.pages.flatMap((page) => page.results) ?? [], [data])
  const totalCount = data?.pages[0]?.totalCount

  const handleEditSubmit = (content: string) => {
    if (!editTarget) return
    editMemory.mutate(
      { id: editTarget.id, content },
      {
        onSuccess: () => {
          setEditTarget(null)
          setEditErrorMessage(null)
        },
        onError: (err) => setEditErrorMessage(err instanceof Error ? err.message : 'Save failed. Please try again.'),
      },
    )
  }

  return (
    <AppShell title="Memory Center" subtitle="Everything Lucy remembers about you — review, edit, or delete any of it.">
      <Tabs value={tab} onChange={(_e, value: MemoryCenterTab) => setTab(value)} sx={{ mb: 3 }}>
        <Tab value="all" label="All memories" />
        <Tab value="approvals" label="Approval queue" />
        <Tab value="preferences" label="Preferences" />
        <Tab value="notifications" label="Notifications" />
        <Tab value="projects" label="Projects" />
      </Tabs>

      {tab === 'all' && (
        <>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5} sx={{ mb: 3, alignItems: { sm: 'center' } }}>
            <TextField
              fullWidth
              size="small"
              placeholder="Search memories"
              aria-label="Search memories"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
              sx={{ flex: 2 }}
              slotProps={{
                input: {
                  startAdornment: (
                    <InputAdornment position="start">
                      <SearchIcon fontSize="small" />
                    </InputAdornment>
                  ),
                },
              }}
            />

            <TextField
              select
              size="small"
              label="Category"
              aria-label="Category"
              value={category ?? ''}
              onChange={(e) => setCategory(e.target.value || null)}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="">All categories</MenuItem>
              {CATEGORY_OPTIONS.map((option) => (
                <MenuItem key={option.value} value={option.value}>
                  {option.label}
                </MenuItem>
              ))}
            </TextField>

            <TextField
              select
              size="small"
              label="State"
              aria-label="State"
              value={state ?? ''}
              onChange={(e) => setState(e.target.value || null)}
              sx={{ minWidth: 160 }}
            >
              <MenuItem value="">All states</MenuItem>
              {STATE_OPTIONS.map((option) => (
                <MenuItem key={option.value} value={option.value}>
                  {option.label}
                </MenuItem>
              ))}
            </TextField>
          </Stack>

          {totalCount !== undefined && (
            <Stack sx={{ mb: 1 }}>
              <span>{totalCount} memor{totalCount === 1 ? 'y' : 'ies'}</span>
            </Stack>
          )}

          <MemoryList
            memories={memories}
            isLoading={isLoading}
            isFiltered={isFiltered}
            hasNextPage={hasNextPage}
            isFetchingNextPage={isFetchingNextPage}
            onFetchNextPage={() => fetchNextPage()}
            onEdit={(memory) => setEditTarget(memory)}
            onDelete={(memory) => setDeleteTarget(memory)}
          />
        </>
      )}

      {tab === 'approvals' && <MemoryApprovalQueue />}
      {tab === 'preferences' && <MemoryPreferencesPanel />}
      {tab === 'notifications' && <MemoryNotificationList />}
      {tab === 'projects' && <ProjectManagementPanel />}

      <MemoryEditDialog
        key={editTarget?.id ?? 'none'}
        open={editTarget !== null}
        memory={editTarget ?? undefined}
        submitting={editMemory.isPending}
        errorMessage={editErrorMessage}
        onSubmit={handleEditSubmit}
        onClose={() => {
          setEditTarget(null)
          setEditErrorMessage(null)
        }}
      />

      <ConfirmDialog
        open={deleteTarget !== null}
        title="Delete this memory?"
        description="Lucy will no longer use this in future conversations. This cannot be undone."
        confirmLabel="Delete"
        onCancel={() => setDeleteTarget(null)}
        onConfirm={() => {
          if (!deleteTarget) return
          const id = deleteTarget.id
          setDeleteTarget(null)
          deleteMemory.mutate(id, {
            onError: (err) => setErrorMessage(err instanceof Error ? err.message : 'Delete failed. Please try again.'),
          })
        }}
      />

      <Snackbar open={Boolean(errorMessage)} autoHideDuration={5000} onClose={() => setErrorMessage(null)}>
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
