import AddIcon from '@mui/icons-material/Add'
import ArrowDownwardIcon from '@mui/icons-material/ArrowDownward'
import ArrowUpwardIcon from '@mui/icons-material/ArrowUpward'
import FolderOpenIcon from '@mui/icons-material/FolderOpenOutlined'
import GridViewIcon from '@mui/icons-material/GridView'
import SearchIcon from '@mui/icons-material/Search'
import ViewListIcon from '@mui/icons-material/ViewList'
import {
  Alert,
  Box,
  Button,
  IconButton,
  InputAdornment,
  MenuItem,
  Snackbar,
  Stack,
  Tab,
  Tabs,
  TextField,
  ToggleButton,
  ToggleButtonGroup,
} from '@mui/material'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { EmptyState } from '../../../components/EmptyState'
import * as knowledgeBasesApi from '../api/knowledgeBasesApi'
import type { KnowledgeBaseSort, KnowledgeBaseSummary } from '../api/knowledgeBasesApi'
import { KnowledgeBaseCard } from '../components/KnowledgeBaseCard'
import {
  KnowledgeBaseEditDialog,
  type KnowledgeBaseFormValues,
} from '../components/KnowledgeBaseEditDialog'
import { KnowledgeBaseStatCards } from '../components/KnowledgeBaseStatCards'
import { ConfirmDialog } from '../../../components/ConfirmDialog'
import { useKnowledgeBaseDashboardSummary } from '../hooks/useKnowledgeBases'
import { useSearchKnowledgeBases } from '../hooks/useSearchKnowledgeBases'
import { useKnowledgeBaseCategories } from '../hooks/useKnowledgeBaseTaxonomy'
import { useKnowledgeBaseDashboardStore } from '../store/knowledgeBaseDashboardStore'
import {
  useActivateKnowledgeBase,
  useArchiveKnowledgeBase,
  useCreateKnowledgeBase,
  useDeleteKnowledgeBase,
  useDuplicateKnowledgeBase,
  useFavoriteKnowledgeBase,
  usePinKnowledgeBase,
  usePurgeKnowledgeBase,
  useRestoreKnowledgeBase,
  useUnfavoriteKnowledgeBase,
  useUnpinKnowledgeBase,
  useUpdateKnowledgeBaseDetails,
} from '../hooks/useKnowledgeBaseMutations'

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  link.click()
  URL.revokeObjectURL(url)
}

/** A dashboard section (FR-027) — `Recent`/`Favorites`/`Pinned` are not distinct backend views, just `view=Active` combined with a forced sort or filter (see `KnowledgeBaseDashboardSummaryDto`'s doc comment); `Archived`/`Deleted` map directly to `KnowledgeBaseListView`. */
type DashboardSection = 'Active' | 'Recent' | 'Favorites' | 'Pinned' | 'Archived' | 'Deleted'

const SECTION_TABS: { value: DashboardSection; label: string }[] = [
  { value: 'Active', label: 'Active' },
  { value: 'Recent', label: 'Recent' },
  { value: 'Favorites', label: 'Favorites' },
  { value: 'Pinned', label: 'Pinned' },
  { value: 'Archived', label: 'Archived' },
  { value: 'Deleted', label: 'Deleted' },
]

const SORT_OPTIONS: { value: KnowledgeBaseSort; label: string }[] = [
  { value: 'Name', label: 'Name' },
  { value: 'RecentlyUpdated', label: 'Recently updated' },
  { value: 'Created', label: 'Created' },
  { value: 'DocumentCount', label: 'Document count' },
  { value: 'StorageSize', label: 'Storage size' },
]

/**
 * The Knowledge Base workspace dashboard (FR-026). Search, category/tag filters, sort,
 * grid/list toggle, statistics cards, and dedicated Recent/Favorites/Pinned sections are US4/
 * US5's discovery layer over US1's core create/edit/delete/restore/permanently-delete
 * lifecycle. The folder tree and document upload live on `KnowledgeBaseDetailPage` (US2),
 * reached by opening a card, not this page.
 */
export function KnowledgeBaseDashboardPage() {
  const navigate = useNavigate()
  const [section, setSection] = useState<DashboardSection>('Active')
  const {
    query,
    categoryId,
    tag,
    sort,
    sortDescending,
    layout,
    setQuery,
    setCategoryId,
    setTag,
    setSort,
    setSortDescending,
    setLayout,
  } = useKnowledgeBaseDashboardStore()

  const isFilteredOrSearched = query.trim() !== '' || Boolean(categoryId) || Boolean(tag?.trim())

  const searchParams = {
    view:
      section === 'Archived'
        ? ('Archived' as const)
        : section === 'Deleted'
          ? ('Deleted' as const)
          : ('Active' as const),
    q: query.trim() || undefined,
    categoryId: categoryId ?? undefined,
    tag: tag?.trim() || undefined,
    favorite: section === 'Favorites' || undefined,
    pinned: section === 'Pinned' || undefined,
    sort: section === 'Recent' ? ('RecentlyUpdated' as const) : sort,
    sortDescending: section === 'Recent' ? true : sortDescending,
  }

  const { data, isLoading, fetchNextPage, hasNextPage, isFetchingNextPage } =
    useSearchKnowledgeBases(searchParams)
  const { data: dashboardSummary, isLoading: isSummaryLoading } = useKnowledgeBaseDashboardSummary()
  const { data: categories } = useKnowledgeBaseCategories()
  const createKnowledgeBase = useCreateKnowledgeBase()
  const updateKnowledgeBase = useUpdateKnowledgeBaseDetails()
  const deleteKnowledgeBase = useDeleteKnowledgeBase()
  const restoreKnowledgeBase = useRestoreKnowledgeBase()
  const activateKnowledgeBase = useActivateKnowledgeBase()
  const archiveKnowledgeBase = useArchiveKnowledgeBase()
  const purgeKnowledgeBase = usePurgeKnowledgeBase()
  const favoriteKnowledgeBase = useFavoriteKnowledgeBase()
  const unfavoriteKnowledgeBase = useUnfavoriteKnowledgeBase()
  const pinKnowledgeBase = usePinKnowledgeBase()
  const unpinKnowledgeBase = useUnpinKnowledgeBase()
  const duplicateKnowledgeBase = useDuplicateKnowledgeBase()

  const [dialogState, setDialogState] = useState<'closed' | 'create' | KnowledgeBaseSummary>(
    'closed',
  )
  const [dialogErrorMessage, setDialogErrorMessage] = useState<string | null>(null)
  const [purgeTarget, setPurgeTarget] = useState<KnowledgeBaseSummary | null>(null)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

  const reportError = (err: unknown) =>
    setErrorMessage(err instanceof Error ? err.message : 'Action failed. Please try again.')

  const closeDialog = () => {
    setDialogState('closed')
    setDialogErrorMessage(null)
  }

  const handleExport = async (knowledgeBase: KnowledgeBaseSummary) => {
    try {
      const blob = await knowledgeBasesApi.exportKnowledgeBase(knowledgeBase.id)
      downloadBlob(blob, `${knowledgeBase.name || 'knowledge-base'}.json`)
    } catch (err) {
      reportError(err)
    }
  }

  const handleSubmit = (values: KnowledgeBaseFormValues) => {
    const input = {
      name: values.name,
      description: values.description || null,
      color: values.color || null,
      icon: values.icon || null,
      categoryId: values.categoryId,
      tags: values.tags,
    }

    const action =
      dialogState !== 'closed' && dialogState !== 'create'
        ? updateKnowledgeBase.mutateAsync({ id: dialogState.id, input })
        : createKnowledgeBase.mutateAsync(input)

    // Kept open on failure so the error is visible right next to the fields the user just
    // filled in, not just a toast (constitution §2.VIII No Silent Failures) — closed only on
    // success.
    action
      .then(() => closeDialog())
      .catch((err: unknown) =>
        setDialogErrorMessage(
          err instanceof Error ? err.message : 'Save failed. Please try again.',
        ),
      )
  }

  const knowledgeBases = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])
  const editingKnowledgeBase =
    dialogState !== 'closed' && dialogState !== 'create' ? dialogState : undefined
  const showFavoritePinToggles = section !== 'Deleted'
  const categoryNamesById = useMemo(
    () => new Map((categories ?? []).map((c) => [c.id, c.name])),
    [categories],
  )

  return (
    <AppShell
      title="Knowledge Bases"
      subtitle="Organize documents into private, purpose-built containers."
      actions={
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => setDialogState('create')}
        >
          New Knowledge Base
        </Button>
      }
    >
      <KnowledgeBaseStatCards summary={dashboardSummary} isLoading={isSummaryLoading} />

      <Tabs
        value={section}
        onChange={(_e, value: DashboardSection) => setSection(value)}
        sx={{ mb: 2 }}
      >
        {SECTION_TABS.map((tab) => (
          <Tab key={tab.value} value={tab.value} label={tab.label} />
        ))}
      </Tabs>

      <Stack
        direction={{ xs: 'column', sm: 'row' }}
        spacing={1.5}
        sx={{ mb: 3, alignItems: { sm: 'center' } }}
      >
        <TextField
          fullWidth
          size="small"
          placeholder="Search by name, description, or tag"
          aria-label="Search knowledge bases"
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
          value={categoryId ?? ''}
          onChange={(e) => setCategoryId(e.target.value || null)}
          sx={{ minWidth: 160 }}
        >
          <MenuItem value="">All categories</MenuItem>
          {(categories ?? []).map((category) => (
            <MenuItem key={category.id} value={category.id}>
              {category.name}
            </MenuItem>
          ))}
        </TextField>

        <TextField
          size="small"
          placeholder="Filter by tag"
          aria-label="Filter by tag"
          value={tag ?? ''}
          onChange={(e) => setTag(e.target.value || null)}
          sx={{ flex: 1 }}
        />

        <TextField
          select
          size="small"
          label="Sort"
          aria-label="Sort knowledge bases"
          value={sort}
          disabled={section === 'Recent'}
          onChange={(e) => setSort(e.target.value as KnowledgeBaseSort)}
          sx={{ minWidth: 180 }}
        >
          {SORT_OPTIONS.map((option) => (
            <MenuItem key={option.value} value={option.value}>
              {option.label}
            </MenuItem>
          ))}
        </TextField>

        <IconButton
          aria-label={sortDescending ? 'Sort ascending' : 'Sort descending'}
          onClick={() => setSortDescending(!sortDescending)}
          disabled={section === 'Recent'}
        >
          {sortDescending ? (
            <ArrowDownwardIcon fontSize="small" />
          ) : (
            <ArrowUpwardIcon fontSize="small" />
          )}
        </IconButton>

        <ToggleButtonGroup
          exclusive
          size="small"
          value={layout}
          onChange={(_e, value: 'grid' | 'list' | null) => value && setLayout(value)}
        >
          <ToggleButton value="grid" aria-label="Grid view">
            <GridViewIcon fontSize="small" />
          </ToggleButton>
          <ToggleButton value="list" aria-label="List view">
            <ViewListIcon fontSize="small" />
          </ToggleButton>
        </ToggleButtonGroup>
      </Stack>

      {!isLoading && knowledgeBases.length === 0 && (
        <EmptyState
          icon={<FolderOpenIcon fontSize="inherit" />}
          title={
            isFilteredOrSearched
              ? 'No matching knowledge bases'
              : section === 'Active'
                ? 'No knowledge bases yet'
                : `No ${section.toLowerCase()} knowledge bases`
          }
          description={
            isFilteredOrSearched
              ? 'Try a different search term or filter.'
              : section === 'Active'
                ? 'Create one to start grounding chats in your own documents.'
                : undefined
          }
          action={
            !isFilteredOrSearched && section === 'Active' ? (
              <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialogState('create')}>
                New Knowledge Base
              </Button>
            ) : undefined
          }
        />
      )}

      <Box
        sx={
          layout === 'grid'
            ? {
                display: 'grid',
                gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr', md: '1fr 1fr 1fr' },
                gap: 2,
              }
            : { display: 'flex', flexDirection: 'column', gap: 1 }
        }
      >
        {knowledgeBases.map((knowledgeBase) => (
          <KnowledgeBaseCard
            key={knowledgeBase.id}
            knowledgeBase={knowledgeBase}
            categoryName={
              knowledgeBase.categoryId ? categoryNamesById.get(knowledgeBase.categoryId) : undefined
            }
            onOpen={
              knowledgeBase.isDeleted
                ? undefined
                : () => navigate(`/knowledge-bases/${knowledgeBase.id}`)
            }
            onEdit={() => setDialogState(knowledgeBase)}
            onActivate={() =>
              activateKnowledgeBase.mutate(knowledgeBase.id, { onError: reportError })
            }
            onArchive={() =>
              archiveKnowledgeBase.mutate(knowledgeBase.id, { onError: reportError })
            }
            onDelete={() => deleteKnowledgeBase.mutate(knowledgeBase.id, { onError: reportError })}
            onRestore={() =>
              restoreKnowledgeBase.mutate(knowledgeBase.id, { onError: reportError })
            }
            onPurge={() => setPurgeTarget(knowledgeBase)}
            onToggleFavorite={
              showFavoritePinToggles
                ? () =>
                    (knowledgeBase.isFavorite
                      ? unfavoriteKnowledgeBase
                      : favoriteKnowledgeBase
                    ).mutate(knowledgeBase.id, {
                      onError: reportError,
                    })
                : undefined
            }
            onTogglePin={
              showFavoritePinToggles
                ? () =>
                    (knowledgeBase.isPinned ? unpinKnowledgeBase : pinKnowledgeBase).mutate(
                      knowledgeBase.id,
                      {
                        onError: reportError,
                      },
                    )
                : undefined
            }
            onDuplicate={
              knowledgeBase.isDeleted
                ? undefined
                : () => duplicateKnowledgeBase.mutate(knowledgeBase.id, { onError: reportError })
            }
            onExport={knowledgeBase.isDeleted ? undefined : () => handleExport(knowledgeBase)}
          />
        ))}
      </Box>

      {hasNextPage && (
        <Box sx={{ display: 'flex', justifyContent: 'center', mt: 3 }}>
          <Button variant="outlined" onClick={() => fetchNextPage()} loading={isFetchingNextPage}>
            Load more
          </Button>
        </Box>
      )}

      <KnowledgeBaseEditDialog
        key={editingKnowledgeBase?.id ?? 'create'}
        open={dialogState !== 'closed'}
        knowledgeBase={editingKnowledgeBase}
        submitting={createKnowledgeBase.isPending || updateKnowledgeBase.isPending}
        errorMessage={dialogErrorMessage}
        onSubmit={handleSubmit}
        onClose={closeDialog}
      />

      <ConfirmDialog
        open={purgeTarget !== null}
        title="Permanently delete this knowledge base?"
        description="This cannot be undone. The knowledge base, its folders, and all its documents will be permanently removed."
        confirmLabel="Delete permanently"
        onCancel={() => setPurgeTarget(null)}
        onConfirm={() => {
          if (!purgeTarget) return
          const id = purgeTarget.id
          setPurgeTarget(null)
          purgeKnowledgeBase.mutate(id, { onError: reportError })
        }}
      />

      <Snackbar
        open={Boolean(errorMessage)}
        autoHideDuration={5000}
        onClose={() => setErrorMessage(null)}
      >
        <Alert severity="error" variant="filled" onClose={() => setErrorMessage(null)}>
          {errorMessage}
        </Alert>
      </Snackbar>
    </AppShell>
  )
}
