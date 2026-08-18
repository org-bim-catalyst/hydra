import AddIcon from '@mui/icons-material/Add'
import FileDownloadOutlinedIcon from '@mui/icons-material/FileDownloadOutlined'
import FileUploadOutlinedIcon from '@mui/icons-material/FileUploadOutlined'
import PushPinIcon from '@mui/icons-material/PushPin'
import PushPinOutlinedIcon from '@mui/icons-material/PushPinOutlined'
import StarIcon from '@mui/icons-material/Star'
import StarBorderIcon from '@mui/icons-material/StarBorder'
import { Box, Button, Card, CardActionArea, CardContent, Chip, IconButton, Stack, Typography } from '@mui/material'
import { useVirtualizer } from '@tanstack/react-virtual'
import { useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router'
import { AppShell } from '../../../components/AppShell'
import { EmptyState } from '../../../components/EmptyState'
import type { PromptListItem } from '../api/promptsApi'
import { ExportDialog } from '../components/ExportDialog'
import { FolderTree } from '../components/FolderTree'
import { ImportDialog } from '../components/ImportDialog'
import { PromptFilters } from '../components/PromptFilters'
import { DEFAULT_PROMPT_FILTERS, type PromptFiltersState } from '../components/promptFiltersState'
import { useSetFavorite, useSetPinned } from '../hooks/usePromptMutations'
import { useSearchPrompts } from '../hooks/usePrompts'

const ROW_HEIGHT = 108

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

  const listParentRef = useRef<HTMLDivElement>(null)
  const virtualizer = useVirtualizer({
    count: prompts.length,
    getScrollElement: () => listParentRef.current,
    estimateSize: () => ROW_HEIGHT,
    overscan: 8,
  })

  const handleScroll = () => {
    const el = listParentRef.current
    if (!el || isFetchingNextPage || !hasNextPage) return
    if (el.scrollTop + el.clientHeight >= el.scrollHeight - 300) {
      void fetchNextPage()
    }
  }

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

          <Box ref={listParentRef} onScroll={handleScroll} data-testid="prompt-list" sx={{ flex: 1, minHeight: 0, overflowY: 'auto' }}>
            <Box sx={{ position: 'relative', height: virtualizer.getTotalSize() }}>
              {virtualizer.getVirtualItems().map((virtualItem) => {
                const prompt = prompts[virtualItem.index]
                return (
                  <Box
                    key={virtualItem.key}
                    data-index={virtualItem.index}
                    ref={virtualizer.measureElement}
                    sx={{ position: 'absolute', top: 0, left: 0, right: 0, transform: `translateY(${virtualItem.start}px)`, pb: 1.5 }}
                  >
                    <PromptCard
                      prompt={prompt}
                      onOpen={() => navigate(`/prompts/${prompt.id}`)}
                      onToggleFavorite={() => setFavorite.mutate({ id: prompt.id, isFavorite: !prompt.isFavorite })}
                      onTogglePinned={() => setPinned.mutate({ id: prompt.id, isPinned: !prompt.isPinned })}
                    />
                  </Box>
                )
              })}
            </Box>
          </Box>
        </Box>
      </Box>

      <ExportDialog open={isExportOpen} onClose={() => setExportOpen(false)} />
      <ImportDialog open={isImportOpen} onClose={() => setImportOpen(false)} />
    </AppShell>
  )
}

interface PromptCardProps {
  prompt: PromptListItem
  onOpen: () => void
  onToggleFavorite: () => void
  onTogglePinned: () => void
}

function PromptCard({ prompt, onOpen, onToggleFavorite, onTogglePinned }: PromptCardProps) {
  return (
    <Card data-testid="prompt-card" variant="outlined">
      <Box sx={{ position: 'relative' }}>
        <CardActionArea onClick={onOpen}>
          <CardContent sx={{ pr: 9 }}>
            <Stack direction="row" sx={{ alignItems: 'center', justifyContent: 'space-between' }}>
              <Typography variant="subtitle1">{prompt.name}</Typography>
              <Chip label={prompt.promptType} size="small" />
            </Stack>
            {prompt.description && (
              <Typography variant="body2" color="text.secondary">
                {prompt.description}
              </Typography>
            )}
            {prompt.tags.length > 0 && (
              <Stack direction="row" spacing={1} sx={{ mt: 1 }}>
                {prompt.tags.map((tag) => (
                  <Chip key={tag} label={tag} size="small" variant="outlined" />
                ))}
              </Stack>
            )}
          </CardContent>
        </CardActionArea>

        <Stack direction="row" sx={{ position: 'absolute', top: 4, right: 4 }}>
          <IconButton
            size="small"
            aria-label={prompt.isPinned ? 'Unpin prompt' : 'Pin prompt'}
            onClick={(e) => {
              e.stopPropagation()
              onTogglePinned()
            }}
          >
            {prompt.isPinned ? <PushPinIcon fontSize="small" color="primary" /> : <PushPinOutlinedIcon fontSize="small" />}
          </IconButton>
          <IconButton
            size="small"
            aria-label={prompt.isFavorite ? 'Unfavorite prompt' : 'Favorite prompt'}
            onClick={(e) => {
              e.stopPropagation()
              onToggleFavorite()
            }}
          >
            {prompt.isFavorite ? <StarIcon fontSize="small" color="warning" /> : <StarBorderIcon fontSize="small" />}
          </IconButton>
        </Stack>
      </Box>
    </Card>
  )
}
