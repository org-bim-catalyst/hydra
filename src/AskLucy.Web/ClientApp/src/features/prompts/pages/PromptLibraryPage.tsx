import AddIcon from '@mui/icons-material/Add'
import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined'
import FileUploadOutlinedIcon from '@mui/icons-material/FileUploadOutlined'
import { Box, Button, Stack, Typography } from '@mui/material'
import { useMemo, useState } from 'react'
import { useNavigate } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { EmptyState } from '../../../components/EmptyState'
import { ExportDialog } from '../components/ExportDialog'
import { FolderTree } from '../components/FolderTree'
import { ImportDialog } from '../components/ImportDialog'
import { PromptFilters } from '../components/PromptFilters'
import { DEFAULT_PROMPT_FILTERS, type PromptFiltersState } from '../components/promptFiltersState'
import { VirtualizedPromptList } from '../components/VirtualizedPromptList'
import { useSetFavorite, useSetPinned } from '../hooks/usePromptMutations'
import { useSearchPrompts } from '../hooks/usePrompts'

/**
 * Prompt Library — folder navigation + search/filters + a virtualized, cursor-paginated result
 * list with favorite/pin toggles (spec.md User Story 4, T094). Upgrades the User Story 1 version
 * of this page (quickstart.md Scenario 1 still passes: create → reopen → reuse).
 */
export function PromptLibraryPage() {
  const navigate = useNavigate()
  const [selectedFolderId, setSelectedFolderId] = useState<string | null>(null)
  const [filters, setFilters] = useState<PromptFiltersState>(DEFAULT_PROMPT_FILTERS)
  const [isExportOpen, setExportOpen] = useState(false)
  const [isImportOpen, setImportOpen] = useState(false)

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useSearchPrompts({
    view: filters.view,
    q: filters.q.trim() || undefined,
    categoryId: filters.categoryId,
    tag: filters.tag,
    folderId: selectedFolderId,
    status: filters.status,
    pageSize: 50,
  })
  const setFavorite = useSetFavorite()
  const setPinned = useSetPinned()

  const prompts = useMemo(() => data?.pages.flatMap((page) => page.items) ?? [], [data])

  return (
    <AppShell>
      <Box sx={{ display: 'flex', height: '100%', minHeight: 0 }}>
        <Box sx={{ width: 260, flexShrink: 0, p: 2, borderRight: 1, borderColor: 'divider', overflowY: 'auto' }}>
          <FolderTree selectedFolderId={selectedFolderId} onSelectFolder={setSelectedFolderId} />
        </Box>

        <Box sx={{ flex: 1, minWidth: 0, display: 'flex', flexDirection: 'column', p: 3 }}>
          <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between', mb: 2 }}>
            <Typography variant="h5">Prompt Library</Typography>
            <Stack direction="row" spacing={1}>
              <Button startIcon={<FileUploadOutlinedIcon />} onClick={() => setImportOpen(true)}>
                Import
              </Button>
              <Button startIcon={<FileDownloadOutlinedIcon />} onClick={() => setExportOpen(true)}>
                Export
              </Button>
              <Button variant="contained" startIcon={<AddIcon />} onClick={() => navigate('/prompts/new')}>
                New Prompt
              </Button>
            </Stack>
          </Stack>

          <Box sx={{ mb: 2 }}>
            <PromptFilters value={filters} onChange={setFilters} />
          </Box>

          {prompts.length === 0 && (
            <EmptyState
              title="No prompts found"
              description={
                filters.q || filters.categoryId || filters.tag || filters.status || selectedFolderId
                  ? 'Try a different search term or clear the active filters.'
                  : 'Create your first reusable prompt to get started.'
              }
            />
          )}

          <VirtualizedPromptList
            prompts={prompts}
            isFetchingNextPage={isFetchingNextPage}
            hasNextPage={hasNextPage}
            onFetchNextPage={() => void fetchNextPage()}
            onOpen={(prompt) => navigate(`/prompts/${prompt.id}`)}
            onToggleFavorite={(prompt) => setFavorite.mutate({ id: prompt.id, isFavorite: !prompt.isFavorite })}
            onTogglePinned={(prompt) => setPinned.mutate({ id: prompt.id, isPinned: !prompt.isPinned })}
          />
        </Box>
      </Box>

      <ExportDialog open={isExportOpen} onClose={() => setExportOpen(false)} />
      <ImportDialog open={isImportOpen} onClose={() => setImportOpen(false)} />
    </AppShell>
  )
}
